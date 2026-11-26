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
/// Each chunk records the page number so answers can cite exact pages.
///
/// Design decisions:
/// - Text is extracted page-by-page rather than concatenating the whole PDF.
///   This keeps page metadata accurate and prevents a chunk from spanning
///   two completely unrelated pages.
/// - Scanned PDFs (images only, no text layer) will produce empty pages.
///   The loader logs a warning and skips those pages.  Full OCR support
///   would require a separate library (e.g. Tesseract) and can be added by
///   implementing a new IDocumentLoader without modifying this class.
/// </summary>
public sealed class PdfLoader : IDocumentLoader
{
    private readonly int _maxChars;
    private readonly int _overlapSentences;

    public PdfLoader(int maxChars = 600, int overlapSentences = 1)
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

        // PdfPig.Open returns a synchronous reader — wrap in a task to keep
        // the interface async for consistency with other loaders.
        using var pdf = PdfDocument.Open(filePath);

        foreach (var page in pdf.GetPages())
        {
            var pageText = ExtractPageText(page);

            if (string.IsNullOrWhiteSpace(pageText))
            {
                // This page has no extractable text layer.  It is likely a
                // scanned image.  Skip it and continue.
                Console.WriteLine($"  [warn] PDF page {page.Number} has no text — skipping.");
                continue;
            }

            // Split this page's text into chunks.  The source label includes
            // both the filename and page number for clear citations.
            var sourceLabel = $"{fileName}, page {page.Number}";
            var chunks      = Chunker.Split(pageText, sourceLabel, _maxChars, _overlapSentences);

            foreach (var chunk in chunks)
            {
                chunk.Id         = chunkId++;
                chunk.SourceType = SourceType.Pdf;
                chunk.FileName   = fileName;
                chunk.PageNumber = page.Number;

                // Ensure Source is populated even if no chapter heading was found.
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

    /// <summary>
    /// Extracts all text from a PDF page, grouping words into lines by their
    /// vertical (Y) position so paragraph structure is preserved.
    /// PdfPig gives us individual words with their bounding boxes; we sort
    /// them by descending Y (top-to-bottom) and group words on the same line
    /// together, then join lines with a newline.  This avoids the flat
    /// word-join that loses paragraph breaks and merges unrelated text.
    /// </summary>
    private static string ExtractPageText(Page page)
    {
        const double lineHeightTolerance = 3.0; // points; words within this Y-delta share a line

        var words = page.GetWords().ToList();
        if (words.Count == 0) return string.Empty;

        // Sort words top-to-bottom (descending Y in PDF coords), left-to-right.
        var sorted = words
            .OrderByDescending(w => w.BoundingBox.Bottom)
            .ThenBy(w => w.BoundingBox.Left)
            .ToList();

        var lines = new List<string>();
        var currentLine = new List<string> { sorted[0].Text };
        var currentY    = sorted[0].BoundingBox.Bottom;

        for (var i = 1; i < sorted.Count; i++)
        {
            var word = sorted[i];
            if (Math.Abs(word.BoundingBox.Bottom - currentY) <= lineHeightTolerance)
            {
                // Same line — add word to current line.
                currentLine.Add(word.Text);
            }
            else
            {
                // New line — flush the current line and start a new one.
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
