namespace BookRag.Config;

/// <summary>
/// Single source of truth for every configurable value in the application.
/// All values are read from environment variables at startup.
/// If a variable is not set, a sensible default is used.
///
/// To configure the application, set these environment variables in your shell
/// or copy .env.example to .env and populate it.
/// </summary>
public class AppConfig
{
    // ── LLM Settings ────────────────────────────────────────────────────────

    /// <summary>Groq API key — required for query mode.</summary>
    public string GroqApiKey { get; }

    /// <summary>
    /// Groq chat model to use.
    /// See https://console.groq.com/docs/models for available IDs.
    /// Default: llama-3.3-70b-versatile (fast, capable, free tier available).
    /// </summary>
    public string GroqModel { get; }

    /// <summary>
    /// LLM temperature. 0.0 = deterministic / factual. 1.0 = creative.
    /// Keep low for RAG to minimise hallucination.
    /// </summary>
    public double LlmTemperature { get; }

    // ── Retrieval Settings ──────────────────────────────────────────────────

    /// <summary>How many top chunks to retrieve per query.</summary>
    public int RetrievalTopK { get; }

    /// <summary>
    /// Minimum cosine similarity (0–1) for a chunk to be included in the
    /// context. Chunks scoring below this are discarded even if they are in
    /// the top-K.  Set to 0 to disable filtering.
    /// </summary>
    public float SimilarityThreshold { get; }

    // ── Chunking Settings ───────────────────────────────────────────────────

    /// <summary>Maximum character count per chunk (approximate).</summary>
    public int ChunkMaxChars { get; }

    /// <summary>
    /// How many sentences of overlap to include at the start of each new
    /// chunk from the previous chunk.  Overlap helps preserve context at
    /// chunk boundaries.
    /// </summary>
    public int ChunkOverlapSentences { get; }

    // ── Storage Settings ────────────────────────────────────────────────────

    /// <summary>
    /// Directory where ingested vector stores are saved.
    /// Defaults to a "data" folder next to the solution root when running
    /// from the standard project layout, or the current directory otherwise.
    /// </summary>
    public string DataDirectory { get; }

    /// <summary>
    /// Name of the active knowledge-base collection.
    /// Different collection names produce different JSON files, which lets
    /// you maintain separate knowledge bases (e.g. "docs", "research").
    /// </summary>
    public string CollectionName { get; }

    // ── Web Loader Settings ─────────────────────────────────────────────────

    /// <summary>HTTP request timeout when fetching web pages (seconds).</summary>
    public int WebTimeoutSeconds { get; }

    // ── Constructor ─────────────────────────────────────────────────────────

    public AppConfig()
    {
        // Required
        GroqApiKey = Env("GROQ_API_KEY", "");

        // LLM
        GroqModel       = Env("GROQ_MODEL",       "llama-3.3-70b-versatile");
        LlmTemperature  = double.Parse(Env("LLM_TEMPERATURE", "0.1"));

        // Retrieval
        RetrievalTopK       = int.Parse(Env("RETRIEVAL_TOP_K",         "5"));
        SimilarityThreshold = float.Parse(Env("SIMILARITY_THRESHOLD",  "0.3"));

        // Chunking
        ChunkMaxChars        = int.Parse(Env("CHUNK_MAX_CHARS",          "600"));
        ChunkOverlapSentences = int.Parse(Env("CHUNK_OVERLAP_SENTENCES", "1"));

        // Storage — resolve the data directory relative to the project root
        // when running from the standard build output location, otherwise
        // fall back to the current working directory.
        var defaultData = ResolveDefaultDataDir();
        DataDirectory  = Env("DATA_DIRECTORY", defaultData);
        CollectionName = Env("COLLECTION_NAME", "default");

        // Web
        WebTimeoutSeconds = int.Parse(Env("WEB_TIMEOUT_SECONDS", "30"));
    }

    // ── Validation ──────────────────────────────────────────────────────────

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> with a clear message if
    /// the API key is missing.  Call this before entering query mode.
    /// </summary>
    public void RequireApiKey()
    {
        if (string.IsNullOrWhiteSpace(GroqApiKey))
            throw new InvalidOperationException(
                "GROQ_API_KEY environment variable is not set. " +
                "Get a free key at https://console.groq.com and set it before querying.");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string Env(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name)?.Trim() is { Length: > 0 } v ? v : fallback;

    /// <summary>
    /// Walks up from the binary's output folder to locate the repository root
    /// "data" directory.  This handles the common case of running from
    /// bin/Debug/net10.0 inside the project.
    /// </summary>
    private static string ResolveDefaultDataDir()
    {
        // Try: <baseDir>/../../../../data  (bin/Debug/net10.0 → repo root)
        var candidate = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data"));

        if (Directory.Exists(candidate))
            return candidate;

        // Fall back to a "data" subfolder next to the current working directory
        return Path.Combine(Directory.GetCurrentDirectory(), "data");
    }
}
