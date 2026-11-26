using BookRag.Models;
using BookRag.Retrieval;

namespace BookRag.Generation;

/// <summary>
/// Orchestrates the Retrieval-Augmented Generation pipeline:
///   1. Retrieve the most relevant chunks for the query.
///   2. Assemble a context block from those chunks (Augmentation).
///   3. Send the context + question to the LLM (Generation).
///   4. Return the answer together with the source chunks.
///
/// Both ILlmClient and Retriever are injected so this class can be tested
/// with mock implementations without hitting real APIs.
/// </summary>
public sealed class RagAnswerer
{
    private readonly Retriever  _retriever;
    private readonly ILlmClient _llm;
    private readonly int        _topK;

    public RagAnswerer(Retriever retriever, ILlmClient llm, int topK = 5)
    {
        _retriever = retriever;
        _llm       = llm;
        _topK      = topK;
    }

    /// <summary>
    /// Answers <paramref name="question"/> using retrieved context from the
    /// vector store.
    /// </summary>
    /// <returns>
    /// The LLM's answer and the list of (chunk, similarity score) pairs that
    /// were used as context.  An empty source list means no chunks cleared the
    /// similarity threshold and the LLM was asked to respond accordingly.
    /// </returns>
    public async Task<(string Answer, List<(Chunk Chunk, float Score)> Sources)>
        AskAsync(string question)
    {
        // 1. RETRIEVAL — find the most relevant chunks
        var hits = _retriever.Search(question, _topK);

        // 2. AUGMENTATION — build a labelled context block
        //    If no chunks cleared the threshold, we still call the LLM and
        //    let the prompt instruction handle the "no information" case.
        string contextBlock;
        if (hits.Count > 0)
        {
            contextBlock = string.Join("\n\n", hits.Select((h, i) =>
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

        // 3. GENERATION — ask the LLM
        var answer = await _llm.ChatAsync(PromptTemplates.RagSystemPrompt, userPrompt);

        return (answer, hits);
    }
}
