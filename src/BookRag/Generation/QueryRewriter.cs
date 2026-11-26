using System.Text.Json;
using BookRag.Generation;

namespace BookRag.Generation;

/// <summary>
/// Uses one LLM call to expand a user question into 3 search-friendly
/// rephrases before retrieval.
///
/// Rephrases cover:
///  • synonyms / expanded names (e.g. "parents" → "mother and father", "mum and dad")
///  • concrete-language version of abstract questions
///  • a direct keyword form of the question
///
/// All 4 queries (original + 3 rewrites) are searched independently and
/// their results merged with RRF, improving recall on paraphrase-sensitive
/// embedding models like all-MiniLM-L6-v2.
/// </summary>
public sealed class QueryRewriter
{
    private readonly ILlmClient _llm;

    public QueryRewriter(ILlmClient llm)
    {
        _llm = llm;
    }

    private const string SystemPrompt =
        "You are a search query expander. " +
        "Given a question about a book, produce exactly 3 short search queries " +
        "that would help retrieve the relevant passage from the book. " +
        "Rules:\n" +
        "1. Each query must be distinct — cover synonyms, name variations, " +
            "or a more concrete phrasing.\n" +
        "2. Keep each query under 15 words.\n" +
        "3. Output ONLY a JSON array of 3 strings, nothing else. " +
            "Example: [\"Lily and James Potter\", \"Harry parents names\", " +
            "\"who are Harry's mum and dad\"]";

    /// <summary>
    /// Returns 3 rewrite queries for the given question, or an empty list
    /// if the LLM call fails (retrieval will fall back to the original query only).
    /// </summary>
    public async Task<List<string>> RewriteAsync(string question)
    {
        try
        {
            var raw = await _llm.ChatAsync(SystemPrompt, $"Question: {question}");

            // Extract the JSON array from the response (the model may add prose)
            var start = raw.IndexOf('[');
            var end   = raw.LastIndexOf(']');

            if (start < 0 || end <= start)
                return [];

            var json    = raw[start..(end + 1)];
            var queries = JsonSerializer.Deserialize<List<string>>(json);

            return queries?
                .Where(q => !string.IsNullOrWhiteSpace(q))
                .Take(3)
                .ToList() ?? [];
        }
        catch
        {
            // Rewriting is a best-effort enhancement; never crash the pipeline.
            return [];
        }
    }
}
