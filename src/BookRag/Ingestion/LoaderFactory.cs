using BookRag.Config;

namespace BookRag.Ingestion;

/// <summary>
/// Selects the right document loader based on the source string.
///
/// Rules:
///   - Starts with "http://" or "https://"  →  WebLoader
///   - Ends with ".pdf" (case-insensitive)  →  PdfLoader
///   - Everything else                       →  TxtLoader
///
/// Adding a new loader (e.g. DocxLoader) only requires a new branch here
/// and a new class implementing IDocumentLoader — no other code changes.
/// </summary>
public static class LoaderFactory
{
    public static IDocumentLoader Create(string source, AppConfig config)
    {
        if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return new WebLoader(
                maxChars:         config.ChunkMaxChars,
                overlapSentences: config.ChunkOverlapSentences,
                timeoutSeconds:   config.WebTimeoutSeconds);
        }

        var ext = Path.GetExtension(source).ToLowerInvariant();

        return ext switch
        {
            ".pdf" => new PdfLoader(
                          maxChars:         config.ChunkMaxChars,
                          overlapSentences: config.ChunkOverlapSentences),

            _      => new TxtLoader(
                          maxChars:         config.ChunkMaxChars,
                          overlapSentences: config.ChunkOverlapSentences)
        };
    }
}
