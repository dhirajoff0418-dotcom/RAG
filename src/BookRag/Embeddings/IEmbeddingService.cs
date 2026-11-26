namespace BookRag.Embeddings;

/// <summary>
/// Contract for any component that converts text into a dense vector.
/// Keeping the RAG pipeline decoupled from the concrete embedding model
/// means you can swap to a remote API (OpenAI, Cohere…) without touching
/// the retrieval or ingestion code.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Converts a piece of text into a fixed-length float vector.
    /// The same model must be used for both ingestion and query time,
    /// otherwise similarity scores will be meaningless.
    /// </summary>
    float[] Embed(string text);
}
