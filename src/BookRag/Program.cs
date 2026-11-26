using System.Text.Json;
using BookRag.Config;
using BookRag.Embeddings;
using BookRag.Generation;
using BookRag.Ingestion;
using BookRag.Models;
using BookRag.Retrieval;
using BookRag.Storage;

// ── Initialise configuration ─────────────────────────────────────────────────
var config = new AppConfig();

// ── Route to the correct sub-command ─────────────────────────────────────────
if (args.Length == 0)
{
    PrintUsage();
    return;
}

// Allow --debug anywhere in the args.
var argList = args.ToList();
if (argList.Remove("--debug"))
    config.Debug = true;

var command = argList.Count > 0 ? argList[0].ToLowerInvariant() : "";

try
{
    switch (command)
    {
        case "ingest":
            await RunIngestAsync(argList.Skip(1).ToArray(), config);
            break;

        case "query":
            await RunQueryAsync(config);
            break;

        case "eval":
            var evalFile = argList.Count > 1 ? argList[1] : null;
            await RunEvalAsync(evalFile, config);
            break;

        default:
            Console.WriteLine($"Unknown command: {command}");
            PrintUsage();
            break;
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"\nError: {ex.Message}");
    Console.ResetColor();
    Environment.Exit(1);
}

// ── Sub-command: ingest ───────────────────────────────────────────────────────

static async Task RunIngestAsync(string[] sources, AppConfig config)
{
    if (sources.Length == 0)
    {
        Console.WriteLine("Usage: bookrag ingest [--fresh] <file-or-url> [<file-or-url> ...]");
        Console.WriteLine("  --fresh   Clear the existing collection before ingesting");
        return;
    }

    // Parse --fresh flag.
    var sourceList = sources.ToList();
    bool fresh = sourceList.Remove("--fresh");

    if (sourceList.Count == 0)
    {
        Console.WriteLine("Error: no source files specified after --fresh.");
        return;
    }

    var store  = new JsonVectorStore(config.DataDirectory, config.CollectionName);
    var chunks = fresh ? [] : await store.LoadAsync();

    if (fresh)
        Console.WriteLine($"Collection: '{config.CollectionName}' (cleared for fresh ingest)");
    else
        Console.WriteLine($"Collection: '{config.CollectionName}' ({chunks.Count} existing chunks)");

    Console.WriteLine($"Store:      {store.FilePath}\n");

    // Check embedding model consistency when appending.
    if (!fresh && chunks.Count > 0)
    {
        var storedModel = await store.LoadEmbeddingModelAsync();
        if (storedModel is not null && storedModel != config.EmbeddingModel)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(
                $"  [warn] Existing index used embedding model '{storedModel}' " +
                $"but current config is '{config.EmbeddingModel}'. " +
                "Consider re-ingesting with --fresh to avoid mixed embeddings.");
            Console.ResetColor();
        }
    }

    int nextId = chunks.Count > 0 ? chunks.Max(c => c.Id) + 1 : 0;

    using var embedder  = new LocalEmbeddingService();
    var       newChunks = new List<Chunk>();

    foreach (var source in sourceList)
    {
        Console.Write($"Loading '{source}' ... ");

        try
        {
            var loader = LoaderFactory.Create(source, config);
            var loaded = await loader.LoadAsync(source);

            Console.WriteLine($"{loaded.Count} chunks extracted");
            Console.Write("  Embedding");

            foreach (var chunk in loaded)
            {
                chunk.Id             = nextId++;
                chunk.EmbeddingModel = config.EmbeddingModel;
                chunk.Embedding      = embedder.Embed(chunk.Text);
                Console.Write(".");
            }

            Console.WriteLine(" done");
            newChunks.AddRange(loaded);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n  [warn] Skipping '{source}': {ex.Message}");
            Console.ResetColor();
        }
    }

    if (newChunks.Count == 0)
    {
        Console.WriteLine("\nNo new chunks were added.");
        return;
    }

    chunks.AddRange(newChunks);
    await store.SaveAsync(chunks);
    await store.SaveEmbeddingModelAsync(config.EmbeddingModel);

    Console.WriteLine($"\nSaved {newChunks.Count} new chunks.");
    Console.WriteLine($"Total chunks in '{config.CollectionName}': {chunks.Count}");
    Console.WriteLine($"Embedding model: {config.EmbeddingModel}");
}

// ── Sub-command: query ────────────────────────────────────────────────────────

static async Task RunQueryAsync(AppConfig config)
{
    config.RequireApiKey();

    var store  = new JsonVectorStore(config.DataDirectory, config.CollectionName);
    var chunks = await store.LoadAsync();

    if (chunks.Count == 0)
    {
        Console.WriteLine($"No chunks found in collection '{config.CollectionName}'.");
        Console.WriteLine("Run 'bookrag ingest <source>' first.");
        return;
    }

    // Warn if the index was built with a different embedding model.
    var storedModel = await store.LoadEmbeddingModelAsync();
    if (storedModel is not null && storedModel != config.EmbeddingModel)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(
            $"ERROR: Index was built with embedding model '{storedModel}' " +
            $"but EMBEDDING_MODEL is set to '{config.EmbeddingModel}'. " +
            "Re-ingest with --fresh or set EMBEDDING_MODEL correctly.");
        Console.ResetColor();
        Environment.Exit(1);
    }

    using var embedder = new LocalEmbeddingService();
    var bm25      = new Bm25Index(chunks, config.Bm25K1, config.Bm25B);
    var retriever = new Retriever(chunks, embedder, bm25,
                                  config.SimilarityThreshold,
                                  config.RetrievalCandidateK);
    var llm       = new GroqClient(config);
    var rewriter  = new QueryRewriter(llm);
    var reranker  = new Reranker(llm);
    var rag       = new RagAnswerer(retriever, llm, rewriter, reranker,
                                    topK:             config.RetrievalTopK,
                                    rerankCandidateK: config.RerankCandidateK,
                                    queryRewrite:     config.QueryRewrite,
                                    rerank:           config.Rerank);

    Console.WriteLine($"Collection '{config.CollectionName}': {chunks.Count} chunks loaded.");
    Console.WriteLine($"Model:      {config.GroqModel}");
    Console.WriteLine($"Settings:   Top-K={config.RetrievalTopK}  " +
                      $"Threshold={config.SimilarityThreshold}  " +
                      $"QueryRewrite={config.QueryRewrite}  " +
                      $"Rerank={config.Rerank}  " +
                      $"Debug={config.Debug}");
    Console.WriteLine("Type a question and press Enter. Leave blank to exit.\n");

    while (true)
    {
        Console.Write("Q> ");
        var question = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(question))
            break;

        var (answer, sources, debugInfo) = await rag.AskAsync(question);

        Console.WriteLine($"\nA> {answer}\n");

        // ── Sources ─────────────────────────────────────────────────────
        if (sources.Count > 0)
        {
            Console.WriteLine("Sources:");
            foreach (var (chunk, score) in sources)
                Console.WriteLine($"  [{score:F3}] {chunk.ToCitationString()}");
        }
        else
        {
            Console.WriteLine("(No matching sources found)");
        }

        // ── Debug output ─────────────────────────────────────────────────
        if (config.Debug)
            PrintDebugInfo(debugInfo);

        Console.WriteLine("\n" + new string('─', 60) + "\n");
    }
}

// ── Sub-command: eval ─────────────────────────────────────────────────────────

static async Task RunEvalAsync(string? evalFilePath, AppConfig config)
{
    config.RequireApiKey();

    // Resolve eval file path.
    if (string.IsNullOrWhiteSpace(evalFilePath))
    {
        var defaultPath = Path.Combine(config.DataDirectory, "eval_hp1.json");
        if (!File.Exists(defaultPath))
        {
            Console.WriteLine(
                "Usage: bookrag eval [path/to/eval.json]\n" +
                $"Default path tried: {defaultPath}");
            return;
        }
        evalFilePath = defaultPath;
    }

    if (!File.Exists(evalFilePath))
    {
        Console.WriteLine($"Eval file not found: {evalFilePath}");
        return;
    }

    var json     = await File.ReadAllTextAsync(evalFilePath);
    var evalItems = JsonSerializer.Deserialize<List<EvalItem>>(json,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    if (evalItems is null || evalItems.Count == 0)
    {
        Console.WriteLine("Eval file is empty or could not be parsed.");
        return;
    }

    var store  = new JsonVectorStore(config.DataDirectory, config.CollectionName);
    var chunks = await store.LoadAsync();

    if (chunks.Count == 0)
    {
        Console.WriteLine($"No chunks in collection '{config.CollectionName}'. Run ingest first.");
        return;
    }

    using var embedder = new LocalEmbeddingService();
    var bm25      = new Bm25Index(chunks, config.Bm25K1, config.Bm25B);
    var retriever = new Retriever(chunks, embedder, bm25,
                                  config.SimilarityThreshold,
                                  config.RetrievalCandidateK);
    var llm       = new GroqClient(config);
    var rewriter  = new QueryRewriter(llm);
    var reranker  = new Reranker(llm);
    var rag       = new RagAnswerer(retriever, llm, rewriter, reranker,
                                    topK:             config.RetrievalTopK,
                                    rerankCandidateK: config.RerankCandidateK,
                                    queryRewrite:     config.QueryRewrite,
                                    rerank:           config.Rerank);

    Console.WriteLine($"\nEvaluating {evalItems.Count} questions against '{config.CollectionName}'...\n");
    Console.WriteLine(new string('═', 70));

    int retrievalHits = 0, answerHits = 0;

    for (int i = 0; i < evalItems.Count; i++)
    {
        var item = evalItems[i];
        Console.WriteLine($"\n[{i + 1}/{evalItems.Count}] {item.Question}");

        var (answer, sources, _) = await rag.AskAsync(item.Question);

        // Check retrieval: did any retrieved chunk contain all expected keywords?
        var retrievedText = string.Join(" ", sources.Select(s => s.Chunk.Text));
        bool retrievalHit = item.ExpectedKeywords.Any(kw =>
            retrievedText.Contains(kw, StringComparison.OrdinalIgnoreCase));

        // Check answer: did the final answer contain any expected keyword?
        bool answerHit = item.ExpectedKeywords.Any(kw =>
            answer.Contains(kw, StringComparison.OrdinalIgnoreCase));

        if (retrievalHit) retrievalHits++;
        if (answerHit)    answerHits++;

        var rIcon = retrievalHit ? "✓" : "✗";
        var aIcon = answerHit    ? "✓" : "✗";

        Console.WriteLine($"  Retrieval [{rIcon}]  Answer [{aIcon}]");
        Console.WriteLine($"  Expected keywords: {string.Join(", ", item.ExpectedKeywords)}");
        Console.WriteLine($"  Answer: {answer[..Math.Min(200, answer.Length)]}...");
    }

    Console.WriteLine("\n" + new string('═', 70));
    Console.WriteLine($"RETRIEVAL HIT RATE: {retrievalHits}/{evalItems.Count} " +
                      $"({100.0 * retrievalHits / evalItems.Count:F0}%)");
    Console.WriteLine($"ANSWER   HIT RATE: {answerHits}/{evalItems.Count} " +
                      $"({100.0 * answerHits / evalItems.Count:F0}%)");
}

// ── Debug printer ────────────────────────────────────────────────────────────

static void PrintDebugInfo(BookRag.Retrieval.DebugInfo d)
{
    Console.ForegroundColor = ConsoleColor.DarkCyan;
    Console.WriteLine("\n── DEBUG ────────────────────────────────────────────");

    if (d.RewrittenQueries.Count > 0)
    {
        Console.WriteLine("Rewritten queries:");
        foreach (var q in d.RewrittenQueries)
            Console.WriteLine($"  • {q}");
    }

    foreach (var (q, hits) in d.VectorHits)
    {
        Console.WriteLine($"\nVector hits for: \"{q}\"");
        foreach (var h in hits) Console.WriteLine($"  {h}");
    }

    foreach (var (q, hits) in d.Bm25Hits)
    {
        Console.WriteLine($"\nBM25 hits for: \"{q}\"");
        foreach (var h in hits) Console.WriteLine($"  {h}");
    }

    if (d.FusedRanking.Count > 0)
    {
        Console.WriteLine("\nFused ranking (RRF):");
        foreach (var r in d.FusedRanking) Console.WriteLine($"  {r}");
    }

    if (d.RerankerScores.Count > 0)
    {
        Console.WriteLine("\nReranker scores (LLM 0–10):");
        foreach (var (citation, score, rank) in
                 d.RerankerScores.OrderByDescending(x => x.LlmScore).Take(10))
            Console.WriteLine($"  [{score}/10] {citation}");
    }

    Console.WriteLine("────────────────────────────────────────────────────");
    Console.ResetColor();
}

// ── Help ─────────────────────────────────────────────────────────────────────

static void PrintUsage()
{
    Console.WriteLine("""
        BookRag — Multipurpose RAG System

        Commands:
          ingest [--fresh] <source> [<source> ...]
                           Add documents to the knowledge base.
                           --fresh clears the collection first.
          query [--debug]  Ask questions interactively.
                           --debug prints retrieval diagnostics.
          eval [file.json] Run retrieval + answer evaluation.
                           Defaults to data/eval_hp1.json.

        Sources:
          path/to/file.txt          Plain text file
          path/to/document.pdf      PDF file
          https://example.com/page  Web page URL

        Key environment variables:
          GROQ_API_KEY            Required for query/eval
          GROQ_MODEL              LLM model (default: openai/gpt-oss-120b)
          COLLECTION_NAME         Knowledge base name (default: default)
          RETRIEVAL_TOP_K         Final chunks sent to LLM (default: 6)
          SIMILARITY_THRESHOLD    Vector filter threshold (default: 0.15)
          RETRIEVAL_CANDIDATE_K   Candidates per search pass (default: 20)
          QUERY_REWRITE           Enable LLM query expansion (default: true)
          RERANK                  Enable LLM reranking (default: true)
          CHUNK_MAX_CHARS         Max chars per chunk body (default: 500)
          DEBUG                   Always-on debug output (default: false)

        Examples:
          dotnet run -- ingest --fresh data/book.pdf
          dotnet run -- query --debug
          dotnet run -- eval data/eval_hp1.json
        """);
}

// ── Eval data model ───────────────────────────────────────────────────────────

record EvalItem(string Question, List<string> ExpectedKeywords);
