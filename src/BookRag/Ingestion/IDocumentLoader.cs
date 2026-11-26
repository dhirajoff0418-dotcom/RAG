using BookRag.Models;

namespace BookRag.Ingestion;

/// <summary>
/// Contract for any component that reads a source (file path or URL) and
/// produces a list of text chunks ready for embedding.
///
/// Each implementation is responsible for:
///   1. Reading the raw content from the source.
///   2. Extracting meaningful text (stripping markup, headers, footers…).
///   3. Splitting the text into appropriately-sized chunks.
///   4. Populating chunk metadata (FileName, PageNumber, Url, Source…).
///
/// Embeddings are NOT generated here — that is handled by the ingestion
/// pipeline after all chunks are collected.
/// </summary>
public interface IDocumentLoader
{
    /// <summary>
    /// Reads the document at <paramref name="source"/> and returns
    /// unemedded chunks.  <paramref name="source"/> is a file path for file
    /// loaders and a URL string for the web loader.
    /// </summary>
    Task<List<Chunk>> LoadAsync(string source);
}
