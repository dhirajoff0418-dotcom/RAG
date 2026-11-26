using System.Text.RegularExpressions;
using BookRag.Models;

namespace BookRag.Ingestion;

/// <summary>
/// Splits cleaned text into overlapping sentence-based chunks and
/// prepends a context prefix to each chunk before embedding.
///
/// Context prefix format:  "[Page N | CHAPTER X: TITLE] "
/// This anchors the embedding so that "who is Hagrid page 50" and
/// "Hagrid, the gamekeeper" land close together in vector space.
///
/// Strategy:
///   1. If the text contains "CHAPTER N: TITLE" headings, split by chapter
///      so chunks do not cross chapter boundaries.
///   2. Within each section, accumulate sentences until the character budget
///      is reached, then start a new chunk with the overlap prefix.
///   3. If a chunk exceeds WARN_CHARS after the prefix is added, print a
///      warning (the embedding model truncates at ~256 tokens / ~900 chars).
/// </summary>
public static class Chunker
{
    /// <summary>
    /// Chunks exceeding this character count after prefix may be truncated
    /// by the embedding model (~256 tokens ≈ ~900 chars).
    /// </summary>
    public const int WarnChars = 900;

    // Matches "CHAPTER 1: SOME TITLE" at the start of a line.
    private static readonly Regex ChapterPattern =
        new(@"(?=^CHAPTER \d+:)", RegexOptions.Multiline | RegexOptions.Compiled);

    // Sentence boundary: ends with . ! or ? followed by whitespace AND
    // the next char is uppercase or a quote — prevents splitting on
    // "Mr. Dursley" or "Mrs. Potter" (title abbreviations).
    private static readonly Regex SentencePattern =
        new(@"(?<![A-Z][a-z]{0,3})(?<=[.!?])\s+(?=[A-Z""'(])", RegexOptions.Compiled);

    /// <summary>
    /// Splits <paramref name="cleanText"/> into chunks, optionally prepending
    /// a page/chapter context prefix to each chunk's text.
    /// </summary>
    /// <param name="cleanText">The text to chunk.</param>
    /// <param name="source">Fallback source label when no chapter heading found.</param>
    /// <param name="maxChars">Max characters per chunk body (before prefix).</param>
    /// <param name="overlapSentences">Sentences of overlap between chunks.</param>
    /// <param name="pageNumber">PDF page number for the prefix (null = omit).</param>
    public static List<Chunk> Split(
        string cleanText,
        string source,
        int    maxChars         = 500,
        int    overlapSentences = 2,
        int?   pageNumber       = null)
    {
        var chunks = new List<Chunk>();
        var id     = 0;

        var sections = ChapterPattern.Split(cleanText)
                                     .Where(s => !string.IsNullOrWhiteSpace(s))
                                     .ToList();

        if (sections.Count == 0)
            sections = [cleanText];

        foreach (var section in sections)
        {
            var parts        = section.Trim().Split('\n', 2);
            var title        = parts[0].Trim();
            var body         = parts.Length > 1 ? parts[1].Trim() : title;
            var sectionLabel = title.StartsWith("CHAPTER") ? title : source;

            // Build a context prefix that anchors the embedding.
            // E.g. "[Page 46 | CHAPTER 4: THE KEEPER OF THE KEYS] "
            var prefix = BuildPrefix(pageNumber, sectionLabel);

            var sentences = SentencePattern.Split(body)
                                           .Select(s => s.Trim())
                                           .Where(s => s.Length > 0)
                                           .ToList();

            var current = new List<string>();
            var len     = 0;

            foreach (var sentence in sentences)
            {
                if (len + sentence.Length > maxChars && current.Count > 0)
                {
                    chunks.Add(MakeChunk(id++, sectionLabel, current, prefix));
                    var overlap = current.TakeLast(overlapSentences).ToList();
                    current     = overlap;
                    len         = overlap.Sum(s => s.Length + 1);
                }

                current.Add(sentence);
                len += sentence.Length + 1;
            }

            if (current.Count > 0)
                chunks.Add(MakeChunk(id++, sectionLabel, current, prefix));
        }

        return chunks;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Chunk MakeChunk(
        int          id,
        string       source,
        List<string> sentences,
        string       prefix)
    {
        var body = string.Join(" ", sentences);
        var text = prefix + body;

        // Warn if the combined text may be truncated by the embedding model.
        if (text.Length > WarnChars)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(
                $"  [warn] Chunk {id} is {text.Length} chars (>{WarnChars}); " +
                "may be truncated by the embedding model.");
            Console.ResetColor();
        }

        return new Chunk { Id = id, Source = source, Text = text };
    }

    private static string BuildPrefix(int? pageNumber, string sectionLabel)
    {
        var parts = new List<string>();

        if (pageNumber.HasValue)
            parts.Add($"Page {pageNumber}");

        // Include chapter title if it looks like one (not a plain filename).
        if (sectionLabel.StartsWith("CHAPTER", StringComparison.OrdinalIgnoreCase))
            parts.Add(sectionLabel);

        return parts.Count > 0
            ? "[" + string.Join(" | ", parts) + "] "
            : string.Empty;
    }
}
