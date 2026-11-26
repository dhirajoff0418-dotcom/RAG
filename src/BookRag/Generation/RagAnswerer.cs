using BookRag.Models;
using BookRag.Retrieval;

namespace BookRag.Generation;

/// <summary>
/// Orchestrates the full Retrieval-Augmented Generation pipeline:
///
///   1. [Optional] Query rewriting — LLM produces 3 rephrasings.
///   2. Hybrid retrieval        — vector + BM25 + RRF per query.
///   3. Multi-query RRF merge   — merge results from all queries.
///   4. [Optional] Reranking    — LLM scores top-N candidates, keeps top-K.
///   5. Augmentation            — assemble context block.
///   6. Generation              — final LLM call with context + question.
/// </summary>
public sealed class RagAnswerer
{
    private readonly Retriever     _retriever;
    private readonly ILlmClient    _llm;
    private readonly QueryRewriter _rewriter;
    private readonly Reranker      _reranker;
    private readonly int           _topK;
    private readonly int           _rerankCandidateK;
    private readonly bool          _queryRewrite;
    private readonly bool          _rerank;

    public RagAnswerer(
        Retriever     retriever,
        ILlmClient    llm,
        QueryRewriter rewriter,
        Reranker      reranker,
        int           topK             = 6,
        int           rerankCandidateK = 20,
        bool          queryRewrite     = true,
        bool          rerank           = true)
    {
        _retriever        = retriever;
        _llm              = llm;
        _rewriter         = rewriter;
        _reranker         = reranker;
        _topK             = topK;
        _rerankCandidateK = rerankCandidateK;
        _queryRewrite     = queryRewrite;
        _rerank           = rerank;
    }

    /// <summary>
    /// Answers <paramref name="question"/> using the full pipeline.
    /// </summary>
    /// <returns>
    /// The LLM answer, the final chunk list used as context, and a
    /// <see cref="DebugInfo"/> bag (always populated, used when debug=true).
    /// </returns>
    public async Task<(string Answer,
                        List<(Chunk Chunk, double Score)> Sources,
                        DebugInfo Debug)>
        AskAsync(string question)
    {
        var debugInfo = new DebugInfo();

        // ── 1. Query rewriting ────────────────────────────────────────────
        var queries = new List<string> { question };

        if (_queryRewrite)
        {
            var rewrites = await _rewriter.RewriteAsync(question);
            debugInfo.RewrittenQueries.AddRange(rewrites);
            queries.AddRange(rewrites);
        }

        // ── 2. Per-query hybrid retrieval ─────────────────────────────────
        var allRankedLists = new List<IEnumerable<(Chunk, double)>>();

        foreach (var q in queries)
        {
            var hits = _retriever.Search(q, debugInfo);
            allRankedLists.Add(hits.Select(h => (h.Chunk, h.RrfScore)));
        }

        // ── 3. Multi-query RRF merge ──────────────────────────────────────
        var merged = RrfMerger.Merge(allRankedLists, _rerankCandidateK);

        // Record fused ranking in debug
        foreach (var (chunk, score) in merged.Take(10))
            debugInfo.FusedRanking.Add($"[{score:F4}] {chunk.ToCitationString()}");

        // ── 4. Reranking ──────────────────────────────────────────────────
        List<(Chunk Chunk, double Score)> finalChunks;

        if (_rerank && merged.Count > 0)
        {
            var reranked = await _reranker.RerankAsync(question, merged, _topK, debugInfo);
            finalChunks  = reranked.Select(r => (r.Chunk, (double)r.Score)).ToList();
        }
        else
        {
            finalChunks = merged.Take(_topK).ToList();
        }

        // ── 5. Augmentation ───────────────────────────────────────────────
        string contextBlock;
        if (finalChunks.Count > 0)
        {
            contextBlock = string.Join("\n\n", finalChunks.Select((h, i) =>
                PromptTemplates.FormatContextChunk(
                    i + 1,
                    h.Chunk.ToCitationString(),
                    h.Chunk.Text)));
        }
        else
        {
            contextBlock = "(No relevant passages found in the knowledge base.)";
        }

        var userPrompt = PromptTemplates.BuildUserPrompt(contextBlock, question);

        // ── 6. Generation ─────────────────────────────────────────────────
        var answer = await _llm.ChatAsync(PromptTemplates.RagSystemPrompt, userPrompt);

        return (answer, finalChunks, debugInfo);
    }
}
