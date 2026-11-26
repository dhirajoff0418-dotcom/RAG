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
        "You are an assistant that answers questions about a book. " +
"You will be given context passages extracted from the book. " +
"Rules:\n" +
"1. Answer ONLY using the provided context passages. Do not use outside " +
    "knowledge, memory of the book, movies, or the internet.\n" +
"2. You may combine information from multiple passages and make reasonable " +
    "inferences (e.g. relationships, feelings, motives) when the passages " +
    "support them, even if not stated directly. Make clear when something is " +
    "an inference.\n" +
"3. If the context does not contain enough information, reply exactly: " +
    "\"I don't have enough information in the provided sources to answer that.\"\n" +
"4. Never contradict the context and never invent details, quotes, or page numbers.\n" +
"5. Keep answers concise. End with a 'Sources:' section listing the passages used.";

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
