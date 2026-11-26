using System.Text.Json;
using BookRag.Models;
using BookRag.Retrieval;

namespace BookRag.Generation;

/// <summary>
/// Reranks a list of retrieved chunks using a single LLM call.
///
/// The LLM receives the question and each chunk (numbered), then returns a
/// JSON array of relevance scores (0–10).  The top-K chunks by score
/// become the final context.
///
/// This corrects cases where BM25 or vector search surface a chunk that
/// mentions the query term incidentally (low relevance) while burying the
/// truly relevant chunk at rank 8 or 10.
///
/// The LLM call is cheap — the prompt is typically &lt;3 K tokens — and
/// adds ~0.5–1 s of latency, well worth the precision gain.
/// </summary>
public sealed class Reranker
{
    private readonly ILlmClient _llm;

    public Reranker(ILlmClient llm) => _llm = llm;

    private const string SystemPrompt =
        "You are a passage relevance scorer. " +
        "You will be given a question and a list of numbered text passages. " +
        "For each passage, output a relevance score from 0 (completely irrelevant) " +
        "to 10 (directly answers the question). " +
        "Output ONLY a JSON array of integers in the same order as the passages. " +
        "Example for 3 passages: [8, 2, 5]";

    /// <summary>
    /// Scores <paramref name="candidates"/> for relevance to
    /// <paramref name="question"/> and returns them sorted by descending score,
    /// capped at <paramref name="topK"/>.
    ///
    /// Also records results in <paramref name="debugInfo"/> when non-null.
    /// </summary>
    public async Task<List<(Chunk Chunk, int Score)>> RerankAsync(
        string question,
        List<(Chunk Chunk, double RrfScore)> candidates,
        int        topK,
        DebugInfo? debugInfo = null)
    {
        if (candidates.Count == 0) return [];

        // Build a numbered passage list for the prompt.
        var passageLines = candidates
            .Select((c, i) => $"[{i + 1}] {c.Chunk.Text}")
            .ToList();

        var userPrompt =
            $"Question: {question}\n\nPassages:\n{string.Join("\n\n", passageLines)}";

        int[] scores;
        try
        {
            var raw = await _llm.ChatAsync(SystemPrompt, userPrompt);

            var start = raw.IndexOf('[');
            var end   = raw.LastIndexOf(']');

            if (start < 0 || end <= start)
                throw new InvalidOperationException("No JSON array in response");

            var json = raw[start..(end + 1)];
            scores = JsonSerializer.Deserialize<int[]>(json)
                     ?? throw new InvalidOperationException("Null deserialisation");
        }
        catch
        {
            // If reranking fails, fall back to the RRF order.
            var fallback = candidates.Take(topK)
                                     .Select(c => (c.Chunk, 0))
                                     .ToList();
            debugInfo?.RerankerScores.Add(("reranker failed — using RRF order", 0, 0));
            return fallback;
        }

        // Pair scores with chunks, handling mismatched array lengths gracefully.
        var ranked = candidates
            .Select((c, i) => (c.Chunk, Score: i < scores.Length ? scores[i] : 0))
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();

        // Populate debug info.
        if (debugInfo is not null)
        {
            int rank = 1;
            foreach (var (c, i) in candidates.Select((c, i) => (c, i)))
            {
                int s = i < scores.Length ? scores[i] : 0;
                debugInfo.RerankerScores.Add((c.Chunk.ToCitationString(), s, rank++));
            }
        }

        return ranked;
    }
}
