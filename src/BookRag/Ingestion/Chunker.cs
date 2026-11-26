using System.Text.RegularExpressions;
using BookRag.Models;

namespace BookRag.Ingestion;

/// <summary>
/// Splits cleaned text into overlapping sentence-based chunks.
///
/// Strategy:
///   1. If the text contains "CHAPTER N: TITLE" headings, split by chapter
///      so chunks do not cross chapter boundaries.  This preserves semantic
///      coherence and makes citations more accurate.
///   2. Within each section, accumulate sentences until the character budget
///      is reached, then start a new chunk.
///   3. The last N sentences of the previous chunk are repeated at the start
///      of the next chunk (overlap) so that context is not lost at boundaries.
///
/// ChunkMaxChars and overlapSentences are configurable so you can tune
/// retrieval quality without touching this code.
/// </summary>
public static class Chunker
{
    // Matches "CHAPTER 1: SOME TITLE" at the start of a line.
    private static readonly Regex ChapterPattern =
        new(@"(?=^CHAPTER \d+:)", RegexOptions.Multiline | RegexOptions.Compiled);

    // Sentence boundary: ends with . ! or ? followed by whitespace,
    // but NOT after common abbreviations like Mr. Mrs. Dr. Jr. Sr. etc.
    // The negative lookbehind prevents splitting on "Mr. " or "Mrs. ".
    private static readonly Regex SentencePattern =
        new(@"(?<![A-Z][a-z]{0,3})(?<=[.!?])\s+(?=[A-Z""'(])", RegexOptions.Compiled);

    /// <summary>
    /// Splits <paramref name="cleanText"/> into chunks.
    /// <paramref name="source"/> is used to populate <see cref="Chunk.Source"/>
    /// when no chapter headings are found.
    /// </summary>
    public static List<Chunk> Split(
        string cleanText,
        string source,
        int    maxChars          = 600,
        int    overlapSentences  = 1)
    {
        var chunks = new List<Chunk>();
        var id     = 0;

        // If the text looks like a chaptered book, split by chapter heading
        // so chunks respect document structure.
        var sections = ChapterPattern.Split(cleanText)
                                     .Where(s => !string.IsNullOrWhiteSpace(s))
                                     .ToList();

        // If there are no chapter markers, treat the whole text as one section.
        if (sections.Count == 0)
            sections = [cleanText];

        foreach (var section in sections)
        {
            // Extract the section title (first line) and body (rest).
            var parts  = section.Trim().Split('\n', 2);
            var title  = parts[0].Trim();
            var body   = parts.Length > 1 ? parts[1].Trim() : title;

            // Use the provided source label if no chapter title was found.
            var sectionLabel = title.StartsWith("CHAPTER") ? title : source;

            // Split the body into individual sentences.
            var sentences = SentencePattern.Split(body)
                                           .Select(s => s.Trim())
                                           .Where(s => s.Length > 0)
                                           .ToList();

            var current = new List<string>();
            var len     = 0;

            foreach (var sentence in sentences)
            {
                // If adding this sentence would exceed the budget, flush the
                // current chunk and start a new one with the overlap prefix.
                if (len + sentence.Length > maxChars && current.Count > 0)
                {
                    chunks.Add(MakeChunk(id++, sectionLabel, current));

                    // Keep the last N sentences to provide context overlap.
                    var overlap = current.TakeLast(overlapSentences).ToList();
                    current     = overlap;
                    len         = overlap.Sum(s => s.Length + 1);
                }

                current.Add(sentence);
                len += sentence.Length + 1;
            }

            // Flush whatever remains in the last chunk.
            if (current.Count > 0)
                chunks.Add(MakeChunk(id++, sectionLabel, current));
        }

        return chunks;
    }

    private static Chunk MakeChunk(int id, string source, List<string> sentences) => new()
    {
        Id     = id,
        Source = source,
        Text   = string.Join(" ", sentences)
    };
}
