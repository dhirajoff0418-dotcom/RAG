using BookRag.Embeddings;
using BookRag.Models;

namespace BookRag.Retrieval;

/// <summary>
/// Performs hybrid semantic + keyword search over the chunk corpus.
///
/// Pipeline for a single query string:
///   1. Dense vector search  — embed the query, cosine-score every chunk,
///      keep top-N above the similarity threshold.
///   2. BM25 keyword search  — tokenise the query, BM25-score every chunk,
///      keep top-N.
///   3. Reciprocal Rank Fusion — merge both ranked lists into one.
///   4. Return up to candidateK results (the caller may rerank further).
///
/// For multi-query retrieval the caller runs Search() per rewrite and
/// merges the resulting lists again with RrfMerger before reranking.
/// </summary>
public sealed class Retriever
{
    private readonly List<Chunk>     _chunks;
    private readonly IEmbeddingService _embedder;
    private readonly Bm25Index        _bm25;
    private readonly float            _similarityThreshold;
    private readonly int              _candidateK;

    public Retriever(
        List<Chunk>      chunks,
        IEmbeddingService embedder,
        Bm25Index         bm25,
        float             similarityThreshold = 0.15f,
        int               candidateK          = 20)
    {
        _chunks              = chunks;
        _embedder            = embedder;
        _bm25                = bm25;
        _similarityThreshold = similarityThreshold;
        _candidateK          = candidateK;
    }

    /// <summary>
    /// Runs hybrid search for a single <paramref name="query"/> and returns
    /// up to <see cref="_candidateK"/> merged results with their RRF scores.
    ///
    /// Also populates the <paramref name="debugInfo"/> bag when non-null.
    /// </summary>
    public List<(Chunk Chunk, double RrfScore)> Search(
        string    query,
        DebugInfo? debugInfo = null)
    {
        // ── Dense vector search ───────────────────────────────────────────
        var queryVector = _embedder.Embed(query);

        var vectorHits = _chunks
            .Where(c => c.Embedding is not null)
            .Select(c => (Chunk: c, Score: (double)CosineSimilarity(queryVector, c.Embedding!)))
            .Where(x => x.Score >= _similarityThreshold)
            .OrderByDescending(x => x.Score)
            .Take(_candidateK)
            .Select(x => (x.Chunk, x.Score))
            .ToList();

        // ── BM25 keyword search ───────────────────────────────────────────
        var bm25Hits = _bm25.Search(query, _candidateK);

        // ── Debug output ──────────────────────────────────────────────────
        if (debugInfo is not null)
        {
            debugInfo.VectorHits.Add((query, vectorHits
                .Take(5)
                .Select(h => $"[{h.Score:F3}] {h.Chunk.ToCitationString()}")
                .ToList()));

            debugInfo.Bm25Hits.Add((query, bm25Hits
                .Take(5)
                .Select(h => $"[{h.Score:F3}] {h.Chunk.ToCitationString()}")
                .ToList()));
        }

        // ── RRF merge ─────────────────────────────────────────────────────
        var merged = RrfMerger.Merge(
            new[] {
                vectorHits.Select(h => (h.Chunk, h.Score)),
                bm25Hits.Select(h => (h.Chunk, h.Score))
            },
            _candidateK);

        return merged;
    }

    // ── Math ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cosine similarity = dot(a, b) / (|a| × |b|).
    /// all-MiniLM embeddings are unit-normalised, so this simplifies to a
    /// dot product, but we keep the full formula for correctness with any model.
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
        return denom < 1e-10f ? 0f : dot / denom;
    }
}
