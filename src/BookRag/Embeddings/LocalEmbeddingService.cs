using SmartComponents.LocalEmbeddings;

namespace BookRag.Embeddings;

/// <summary>
/// Generates embeddings using a local ONNX model (all-MiniLM-L6-v2) bundled
/// with the SmartComponents package.  No internet connection or API key is
/// required, making this safe and fast for development and offline use.
///
/// The model produces 384-dimensional vectors.  Cosine similarity works well
/// with these vectors for semantic search.
/// </summary>
public sealed class LocalEmbeddingService : IEmbeddingService, IDisposable
{
    private readonly LocalEmbedder _embedder = new();

    /// <inheritdoc/>
    public float[] Embed(string text) =>
        _embedder.Embed(text).Values.ToArray();

    public void Dispose() => _embedder.Dispose();
}
