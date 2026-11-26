namespace BookRag.Generation;

/// <summary>
/// Contract for any chat-completion LLM provider.
/// Keeping this as an interface means you can swap Groq for OpenAI, Ollama,
/// or a mock in tests without touching the RAG pipeline.
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// Sends a system prompt and a user message to the LLM and returns
    /// the assistant's reply as a plain string.
    /// </summary>
    Task<string> ChatAsync(string systemPrompt, string userPrompt);
}
