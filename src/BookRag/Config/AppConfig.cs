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
    /// Default: openai/gpt-oss-120b (available on the developer plan).
    /// llama-3.3-70b-versatile is now Enterprise-only.
    /// </summary>
    public string GroqModel { get; }

    /// <summary>
    /// LLM temperature. 0.0 = deterministic / factual. 1.0 = creative.
    /// Keep low for RAG to minimise hallucination.
    /// </summary>
    public double LlmTemperature { get; }

    // ── Retrieval Settings ──────────────────────────────────────────────────

    /// <summary>
    /// How many top chunks to pass to the LLM as final context.
    /// </summary>
    public int RetrievalTopK { get; }

    /// <summary>
    /// Minimum cosine similarity (0–1) for a chunk to pass the VECTOR
    /// filter.  Chunks below this are excluded from the vector side only;
    /// BM25 hits are not filtered by this value.
    /// </summary>
    public float SimilarityThreshold { get; }

    /// <summary>
    /// How many candidates each of vector and BM25 search return before RRF
    /// merging.  Higher = more recall, more reranking work.
    /// </summary>
    public int RetrievalCandidateK { get; }

    // ── Hybrid search / BM25 ───────────────────────────────────────────────

    /// <summary>
    /// BM25 k1 parameter (term-frequency saturation). Typical range 1.2–2.0.
    /// </summary>
    public double Bm25K1 { get; }

    /// <summary>BM25 b parameter (length normalisation). Range 0–1.</summary>
    public double Bm25B { get; }

    // ── Query Rewriting ─────────────────────────────────────────────────────

    /// <summary>
    /// When true, the system calls the LLM once before retrieval to produce
    /// 3 search query rewrites, then merges all results with RRF.
    /// </summary>
    public bool QueryRewrite { get; }

    // ── Reranking ───────────────────────────────────────────────────────────

    /// <summary>
    /// When true, the top candidates are reranked with one LLM call that
    /// scores each chunk 0–10 for relevance before selecting top-K.
    /// </summary>
    public bool Rerank { get; }

    /// <summary>
    /// How many merged candidates to send to the reranker.
    /// Only used when RERANK=true.
    /// </summary>
    public int RerankCandidateK { get; }

    // ── Chunking Settings ───────────────────────────────────────────────────

    /// <summary>Maximum character count per chunk (approximate).</summary>
    public int ChunkMaxChars { get; }

    /// <summary>
    /// How many sentences of overlap to include at the start of each new
    /// chunk from the previous chunk.
    /// </summary>
    public int ChunkOverlapSentences { get; }

    // ── Embedding Settings ──────────────────────────────────────────────────

    /// <summary>
    /// Identifier for the embedding model used at ingest time.
    /// Stored in the index file. At query time the system refuses to run
    /// if the current model name differs from the stored one.
    /// Built-in values: "all-MiniLM-L6-v2" (default).
    /// </summary>
    public string EmbeddingModel { get; }

    // ── Storage Settings ────────────────────────────────────────────────────

    /// <summary>
    /// Directory where ingested vector stores are saved.
    /// </summary>
    public string DataDirectory { get; }

    /// <summary>Name of the active knowledge-base collection.</summary>
    public string CollectionName { get; }

    // ── Web Loader Settings ─────────────────────────────────────────────────

    /// <summary>HTTP request timeout when fetching web pages (seconds).</summary>
    public int WebTimeoutSeconds { get; }

    // ── Debug / Eval ────────────────────────────────────────────────────────

    /// <summary>
    /// When true, the query command prints detailed diagnostics:
    /// rewritten queries, vector hits, BM25 hits, fused ranking, reranker
    /// scores.  Set DEBUG=true or pass --debug on the command line.
    /// </summary>
    public bool Debug { get; set; }   // settable so --debug flag can override

    // ── Constructor ─────────────────────────────────────────────────────────

    public AppConfig()
    {
        // Required
        GroqApiKey = Env("GROQ_API_KEY", "");

        // LLM
        GroqModel      = Env("GROQ_MODEL",      "openai/gpt-oss-120b");
        LlmTemperature = double.Parse(Env("LLM_TEMPERATURE", "0.1"));

        // Retrieval
        RetrievalTopK       = int.Parse(Env("RETRIEVAL_TOP_K",         "6"));
        SimilarityThreshold = float.Parse(Env("SIMILARITY_THRESHOLD",  "0.15"));
        RetrievalCandidateK = int.Parse(Env("RETRIEVAL_CANDIDATE_K",   "20"));

        // BM25
        Bm25K1 = double.Parse(Env("BM25_K1", "1.5"));
        Bm25B  = double.Parse(Env("BM25_B",  "0.75"));

        // Query rewriting
        QueryRewrite = bool.Parse(Env("QUERY_REWRITE", "true"));

        // Reranking
        Rerank           = bool.Parse(Env("RERANK",             "true"));
        RerankCandidateK = int.Parse(Env("RERANK_CANDIDATE_K",  "20"));

        // Chunking
        ChunkMaxChars         = int.Parse(Env("CHUNK_MAX_CHARS",          "500"));
        ChunkOverlapSentences = int.Parse(Env("CHUNK_OVERLAP_SENTENCES",  "2"));

        // Embedding model identifier
        EmbeddingModel = Env("EMBEDDING_MODEL", "all-MiniLM-L6-v2");

        // Storage
        var defaultData = ResolveDefaultDataDir();
        DataDirectory  = Env("DATA_DIRECTORY", defaultData);
        CollectionName = Env("COLLECTION_NAME", "default");

        // Web
        WebTimeoutSeconds = int.Parse(Env("WEB_TIMEOUT_SECONDS", "30"));

        // Debug
        Debug = bool.Parse(Env("DEBUG", "false"));
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
    /// "data" directory.
    /// </summary>
    private static string ResolveDefaultDataDir()
    {
        var candidate = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data"));

        if (Directory.Exists(candidate))
            return candidate;

        return Path.Combine(Directory.GetCurrentDirectory(), "data");
    }
}
