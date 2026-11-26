using BookRag.Models;

namespace BookRag.Ingestion;

/// <summary>
/// Loads a plain-text (.txt) file and splits it into chunks.
///
/// Pipeline:
///   Read file → TextCleaner.Clean() → Chunker.Split() → List&lt;Chunk&gt;
///
/// Each chunk records the source filename so answers can cite
/// "book.txt — CHAPTER 3: THE LOCKED ROOM" etc.
/// </summary>
public sealed class TxtLoader : IDocumentLoader
{
    private readonly int _maxChars;
    private readonly int _overlapSentences;

    public TxtLoader(int maxChars = 600, int overlapSentences = 1)
    {
        _maxChars         = maxChars;
        _overlapSentences = overlapSentences;
    }

    /// <inheritdoc/>
    public async Task<List<Chunk>> LoadAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"TXT file not found: {filePath}");

        var raw = await File.ReadAllTextAsync(filePath);

        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException($"TXT file is empty: {filePath}");

        var fileName  = Path.GetFileName(filePath);
        var cleanText = TextCleaner.Clean(raw);

        // Split into chunks, using the filename as the fallback source label.
        var chunks = Chunker.Split(cleanText, fileName, _maxChars, _overlapSentences);

        // Tag every chunk with the source file so citations work.
        foreach (var chunk in chunks)
        {
            chunk.SourceType = SourceType.Text;
            chunk.FileName   = fileName;

            // If the chunker did not assign a chapter label, fall back to
            // the filename so Source is never empty.
            if (string.IsNullOrWhiteSpace(chunk.Source))
                chunk.Source = fileName;
        }

        return chunks;
    }
}
