namespace BookRag.Generation;

/// <summary>
/// All prompt strings live here so they are easy to find, review, and modify
/// without hunting through business logic code.
///
/// Security consideration:
///   Retrieved chunk text is treated as untrusted user/document content and
///   placed inside a clearly labelled "CONTEXT" block, never embedded into
///   the system instructions.  This limits prompt injection — a malicious
///   document cannot overwrite the system role.
/// </summary>
public static class PromptTemplates
{
    /// <summary>
    /// System prompt for the RAG answering mode.
    /// Instructs the model to stay grounded in the provided context and to
    /// cite its sources rather than inventing information.
    /// </summary>
    public const string RagSystemPrompt =
        "You are a helpful assistant answering questions about a book. " +
        "You will be given several context passages extracted from the book. " +
        "Rules:\n" +
        "1. Answer using the context passages. You may also use widely known facts " +
            "about the book that are consistent with the context (e.g. character names, " +
            "relationships) — but never contradict the context.\n" +
        "2. If the answer truly cannot be determined from the context or general knowledge " +
            "of the book, reply: " +
            "\"I don't have enough information in the provided sources to answer that.\"\n" +
        "3. At the end of your answer, list the sources you used in a 'Sources:' section.\n" +
        "4. Keep answers concise and directly relevant to the question.\n" +
        "5. Never make up citations, page numbers, or URLs.";

    /// <summary>
    /// Builds the user message that contains the retrieved context and the
    /// question.  Context is placed before the question to reduce the
    /// likelihood of the model ignoring it.
    ///
    /// Each context block is labelled with its source so the model can
    /// reference it in the Sources section of its answer.
    /// </summary>
    public static string BuildUserPrompt(string contextBlock, string question) =>
        $"CONTEXT:\n{contextBlock}\n\n---\n\nQUESTION: {question}";

    /// <summary>
    /// Formats a single retrieved chunk for inclusion in the context block.
    /// </summary>
    public static string FormatContextChunk(int index, string citation, string text) =>
        $"[Source {index}: {citation}]\n{text}";
}
