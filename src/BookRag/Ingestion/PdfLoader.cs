using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using BookRag.Models;

namespace BookRag.Ingestion;

/// <summary>
/// Loads a PDF file and splits each page's text into chunks.
///
/// Pipeline:
///   PdfPig.Open() → extract text per page → Chunker.Split() per page → List&lt;Chunk&gt;
///
/// Each chunk's text is prefixed with "[Page N]" so the embedding captures
/// location context, and the chunk metadata records the exact page for
/// accurate citations.
/// </summary>
public sealed class PdfLoader : IDocumentLoader
{
    private readonly int _maxChars;
    private readonly int _overlapSentences;

    public PdfLoader(int maxChars = 500, int overlapSentences = 2)
    {
        _maxChars         = maxChars;
        _overlapSentences = overlapSentences;
    }

    /// <inheritdoc/>
    public Task<List<Chunk>> LoadAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"PDF file not found: {filePath}");

        var fileName  = Path.GetFileName(filePath);
        var allChunks = new List<Chunk>();
        var chunkId   = 0;

        using var pdf = PdfDocument.Open(filePath);

        foreach (var page in pdf.GetPages())
        {
            var pageText = ExtractPageText(page);

            if (string.IsNullOrWhiteSpace(pageText))
            {
                Console.WriteLine($"  [warn] PDF page {page.Number} has no text — skipping.");
                continue;
            }

            var sourceLabel = $"{fileName}, page {page.Number}";

            // Pass the page number so Chunker can embed it in the text prefix.
            var chunks = Chunker.Split(
                pageText,
                sourceLabel,
                _maxChars,
                _overlapSentences,
                pageNumber: page.Number);

            foreach (var chunk in chunks)
            {
                chunk.Id         = chunkId++;
                chunk.SourceType = SourceType.Pdf;
                chunk.FileName   = fileName;
                chunk.PageNumber = page.Number;

                if (string.IsNullOrWhiteSpace(chunk.Source))
                    chunk.Source = sourceLabel;
            }

            allChunks.AddRange(chunks);
        }

        if (allChunks.Count == 0)
            throw new InvalidOperationException(
                $"No text could be extracted from '{fileName}'. " +
                "The PDF may be scanned (image-only). OCR is not yet supported.");

        return Task.FromResult(allChunks);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts all text from a PDF page, grouping words into lines by their
    /// vertical (Y) position to preserve paragraph structure.
    /// </summary>
    private static string ExtractPageText(Page page)
    {
        const double lineHeightTolerance = 3.0;

        var words = page.GetWords().ToList();
        if (words.Count == 0) return string.Empty;

        var sorted = words
            .OrderByDescending(w => w.BoundingBox.Bottom)
            .ThenBy(w => w.BoundingBox.Left)
            .ToList();

        var lines       = new List<string>();
        var currentLine = new List<string> { sorted[0].Text };
        var currentY    = sorted[0].BoundingBox.Bottom;

        for (var i = 1; i < sorted.Count; i++)
        {
            var word = sorted[i];
            if (Math.Abs(word.BoundingBox.Bottom - currentY) <= lineHeightTolerance)
            {
                currentLine.Add(word.Text);
            }
            else
            {
                lines.Add(string.Join(" ", currentLine));
                currentLine = [word.Text];
                currentY    = word.BoundingBox.Bottom;
            }
        }

        if (currentLine.Count > 0)
            lines.Add(string.Join(" ", currentLine));

        return string.Join("\n", lines).Trim();
    }
}
