using BookRag.Embeddings;
using BookRag.Models;

namespace BookRag.Retrieval;

/// <summary>
/// Performs in-memory semantic search over a list of embedded chunks.
///
/// Given a query string it:
///   1. Embeds the query with the same model used at ingestion time.
///   2. Scores every chunk by cosine similarity.
///   3. Returns the top-K chunks above the similarity threshold.
///
/// The similarity threshold prevents clearly irrelevant chunks from being
/// included in the LLM context, which reduces hallucination and keeps
/// prompts concise.
/// </summary>
public sealed class Retriever
{
    private readonly List<Chunk>     _chunks;
    private readonly IEmbeddingService _embedder;
    private readonly float           _similarityThreshold;

    public Retriever(List<Chunk> chunks, IEmbeddingService embedder, float similarityThreshold = 0.3f)
    {
        _chunks              = chunks;
        _embedder            = embedder;
        _similarityThreshold = similarityThreshold;
    }

    /// <summary>
    /// Returns up to <paramref name="topK"/> chunks most semantically similar
    /// to <paramref name="query"/>, ordered by descending similarity.
    ///
    /// Chunks with no embedding (Embedding == null) are silently skipped —
    /// they would cause a null-reference crash and should not exist in a
    /// correctly-built index.
    /// </summary>
    public List<(Chunk Chunk, float Score)> Search(string query, int topK = 5)
    {
        var queryVector = _embedder.Embed(query);

        return _chunks
            .Where(c => c.Embedding is not null)             // skip unembedded chunks
            .Select(c => (Chunk: c, Score: CosineSimilarity(queryVector, c.Embedding!)))
            .Where(x => x.Score >= _similarityThreshold)     // discard irrelevant chunks
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();
    }

    // ── Math ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cosine similarity = dot(a, b) / (|a| × |b|).
    /// Returns a value in [-1, 1]; higher means more similar.
    /// All-MiniLM embeddings are already unit-normalised, so this simplifies
    /// to a dot product, but we keep the full formula for correctness.
    /// </summary>
    public static float CosineSimilarity(float[] a, float[] b)
    {
        float dot = 0f, normA = 0f, normB = 0f;

        for (var i = 0; i < a.Length; i++)
        {
            dot   += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);

        // Guard against division by zero (e.g. all-zero embedding).
        return denom < 1e-10f ? 0f : dot / denom;
    }
}
