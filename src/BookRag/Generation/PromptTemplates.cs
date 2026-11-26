namespace BookRag.Generation;

/// <summary>
/// All prompt strings live here so they are easy to find, review, and modify
/// without hunting through business logic code.
/// </summary>
public static class PromptTemplates
{
    /// <summary>
    /// System prompt for the RAG answering mode.
    ///
    /// Two-part answer format:
    ///   "From the book:" — facts stated or strongly implied in the retrieved passages.
    ///   "Interpretation:" — inferences or emotional observations supported by the
    ///                        passages but requiring a small reasoning step.
    ///
    /// If nothing relevant is retrieved the model returns the canned fallback
    /// sentence instead of the two-part format.
    /// </summary>
    public const string RagSystemPrompt =
        "You are a knowledgeable assistant answering questions about a book. " +
        "You will be given several numbered context passages extracted from the book.\n\n" +
        "Rules:\n" +
        "1. Base your answer on the context passages. You may draw clear inferences " +
            "from the passages when the answer is strongly implied.\n" +
        "2. Structure your answer in TWO parts:\n" +
            "   • 'From the book:' — quote or paraphrase facts from the passages.\n" +
            "   • 'Interpretation:' — your reasoning or emotional insight, " +
            "grounded in the passages.\n" +
        "3. If the passages contain no relevant information, reply with EXACTLY:\n" +
            "   \"I don't have enough information in the provided sources to answer that.\"\n" +
        "4. After your answer, add a 'Sources:' section listing only the sources you used.\n" +
        "5. Never invent character names, page numbers, or events not in the passages.";

    /// <summary>
    /// Builds the user message that contains the retrieved context and the question.
    /// Context is placed before the question to reduce the likelihood of the model
    /// ignoring it (lost-in-the-middle effect).
    /// </summary>
    public static string BuildUserPrompt(string contextBlock, string question) =>
        $"CONTEXT PASSAGES:\n{contextBlock}\n\n---\n\nQUESTION: {question}";

    /// <summary>Formats a single retrieved chunk for inclusion in the context block.</summary>
    public static string FormatContextChunk(int index, string citation, string text) =>
        $"[Passage {index} — {citation}]\n{text}";

    // ── Query rewriting ──────────────────────────────────────────────────────
    // (prompt lives in QueryRewriter.cs to keep concerns co-located)

    // ── Reranking ────────────────────────────────────────────────────────────
    // (prompt lives in Reranker.cs)
}
