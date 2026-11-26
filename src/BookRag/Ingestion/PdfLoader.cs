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

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts all text from a PDF page, joining words with spaces.
    /// PdfPig gives us individual words with their positions; we join them
    /// left-to-right, top-to-bottom which is correct for most layouts.
    /// </summary>
    private static string ExtractPageText(Page page)
    {
        // PdfPig's Words() gives us positioned words.  Simple join is good
        // enough for most single-column documents.
        var words = page.GetWords();
        return string.Join(" ", words.Select(w => w.Text)).Trim();
    }
}
