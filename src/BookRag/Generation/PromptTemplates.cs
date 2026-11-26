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
        "You are a helpful assistant that answers questions using ONLY the " +
        "context passages provided below. " +
        "Rules you must follow:\n" +
        "1. Base every claim on the context. Do not use outside knowledge.\n" +
        "2. If the answer is not present in the context, reply exactly: " +
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
