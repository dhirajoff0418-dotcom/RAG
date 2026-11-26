using HtmlAgilityPack;
using BookRag.Models;

namespace BookRag.Ingestion;

/// <summary>
/// Fetches a web page and converts its content into text chunks.
///
/// Pipeline:
///   HTTP GET → HtmlAgilityPack parse → extract title + body text
///   → Chunker.Split() → List&lt;Chunk&gt;
///
/// Noise removal:
///   The loader strips script, style, nav, header, footer, aside, and form
///   elements before extracting text.  This removes most navigation menus,
///   cookie banners, and ads without requiring a full headless browser.
///
/// Security note:
///   Web content is treated as untrusted data.  Chunk text is passed to the
///   LLM inside a "context" block, never as a system instruction, to minimise
///   prompt injection risk.
///
/// Limitations:
///   - JavaScript-rendered content is not supported (requires a headless browser).
///   - robots.txt is respected implicitly via standard HTTP; we do not parse it.
///   - Rate limiting and authentication are out of scope.
/// </summary>
public sealed class WebLoader : IDocumentLoader
{
    private readonly int _maxChars;
    private readonly int _overlapSentences;
    private readonly int _timeoutSeconds;

    // Reuse a single HttpClient across calls (avoid socket exhaustion).
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    // HTML elements that contain no meaningful content for RAG purposes.
    private static readonly HashSet<string> _skippedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "nav", "header", "footer",
        "aside", "form", "noscript", "iframe", "button", "svg"
    };

    public WebLoader(int maxChars = 600, int overlapSentences = 1, int timeoutSeconds = 30)
    {
        _maxChars         = maxChars;
        _overlapSentences = overlapSentences;
        _timeoutSeconds   = timeoutSeconds;

        // Apply the configured timeout.
        _http.Timeout = TimeSpan.FromSeconds(_timeoutSeconds);
    }

    /// <inheritdoc/>
    public async Task<List<Chunk>> LoadAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            throw new ArgumentException($"Invalid URL: {url}. Must be an http or https URL.");
        }

        string html;
        try
        {
            html = await _http.GetStringAsync(url);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Could not fetch '{url}': {ex.Message}", ex);
        }
        catch (TaskCanceledException)
        {
            throw new InvalidOperationException(
                $"Request timed out after {_timeoutSeconds}s for URL: {url}");
        }

        var (title, bodyText) = ParseHtml(html, url);

        if (string.IsNullOrWhiteSpace(bodyText))
            throw new InvalidOperationException(
                $"No readable text found at '{url}'. The page may be JavaScript-rendered.");

        // Split the extracted text into chunks.
        var sourceLabel = string.IsNullOrWhiteSpace(title) ? url : title;
        var chunks      = Chunker.Split(bodyText, sourceLabel, _maxChars, _overlapSentences);

        foreach (var chunk in chunks)
        {
            chunk.SourceType = SourceType.Web;
            chunk.Url        = url;

            if (string.IsNullOrWhiteSpace(chunk.Source))
                chunk.Source = sourceLabel;
        }

        return chunks;
    }

    // ── HTML Parsing ─────────────────────────────────────────────────────────

    private static (string Title, string BodyText) ParseHtml(string html, string url)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Extract page title for use as the source label.
        var title = doc.DocumentNode
                       .SelectSingleNode("//title")
                       ?.InnerText.Trim() ?? url;

        // Remove all elements that are unlikely to contain useful content.
        RemoveNoiseTags(doc.DocumentNode);

        // Try to extract the main content area first (<main>, <article>).
        // Fall back to <body> if neither is present.
        var contentNode =
            doc.DocumentNode.SelectSingleNode("//main")   ??
            doc.DocumentNode.SelectSingleNode("//article") ??
            doc.DocumentNode.SelectSingleNode("//body")    ??
            doc.DocumentNode;

        var text = ExtractText(contentNode);

        return (title, text);
    }

    /// <summary>
    /// Recursively removes all descendant nodes whose tag name is in the
    /// skip list.  Modifying the collection while iterating requires
    /// collecting nodes first.
    /// </summary>
    private static void RemoveNoiseTags(HtmlNode root)
    {
        var toRemove = root.Descendants()
                           .Where(n => n.NodeType == HtmlNodeType.Element &&
                                       _skippedTags.Contains(n.Name))
                           .ToList();

        foreach (var node in toRemove)
            node.Remove();
    }

    /// <summary>
    /// Walks the HTML node tree and collects inner text, normalising
    /// whitespace to produce clean prose suitable for the chunker.
    /// </summary>
    private static string ExtractText(HtmlNode root)
    {
        var lines = new List<string>();

        foreach (var node in root.DescendantsAndSelf())
        {
            if (node.NodeType != HtmlNodeType.Text) continue;

            // HtmlAgilityPack gives raw HTML entities; decode them.
            var text = HtmlEntity.DeEntitize(node.InnerText);

            // Collapse internal whitespace and trim the line.
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

            if (text.Length > 0)
                lines.Add(text);
        }

        // Join lines with a space; the chunker will split on sentence boundaries.
        return string.Join(" ", lines);
    }
}
