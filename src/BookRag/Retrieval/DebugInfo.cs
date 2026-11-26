namespace BookRag.Retrieval;

/// <summary>
/// Collects diagnostic information from a single RAG query so the
/// --debug flag can print it after the answer.
///
/// Fields are populated by Retriever, QueryRewriter, and Reranker;
/// Program.cs reads them for display.
/// </summary>
public sealed class DebugInfo
{
    /// <summary>
    /// Rewritten queries produced by the LLM (empty when QUERY_REWRITE=false).
    /// </summary>
    public List<string> RewrittenQueries { get; } = [];

    /// <summary>
    /// Per-query vector search hits: (queryText, list of "[score] citation" strings).
    /// </summary>
    public List<(string Query, List<string> Hits)> VectorHits { get; } = [];

    /// <summary>
    /// Per-query BM25 search hits: (queryText, list of "[score] citation" strings).
    /// </summary>
    public List<(string Query, List<string> Hits)> Bm25Hits { get; } = [];

    /// <summary>
    /// RRF-fused ranking after merging all per-query results.
    /// Each string is "[rrfScore] citation".
    /// </summary>
    public List<string> FusedRanking { get; } = [];

    /// <summary>
    /// Reranker scores: (citation, llmScore 0–10, finalRank).
    /// Empty when RERANK=false.
    /// </summary>
    public List<(string Citation, int LlmScore, int Rank)> RerankerScores { get; } = [];
}
