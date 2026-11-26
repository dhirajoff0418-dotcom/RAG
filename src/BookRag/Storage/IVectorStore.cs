using BookRag.Models;

namespace BookRag.Storage;

/// <summary>
/// Contract for persisting and loading a collection of embedded chunks.
///
/// The current implementation stores everything in a JSON file.  The
/// interface means you could swap to SQLite, Qdrant, or any other backend
/// without changing the ingestion or retrieval logic.
/// </summary>
public interface IVectorStore
{
    /// <summary>
    /// Loads all chunks from the store.
    /// Returns an empty list if the store does not exist yet.
    /// </summary>
    Task<List<Chunk>> LoadAsync();

    /// <summary>
    /// Persists the given list of chunks, overwriting any existing data.
    /// </summary>
    Task SaveAsync(List<Chunk> chunks);
}
