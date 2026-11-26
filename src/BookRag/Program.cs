using BookRag.Config;
using BookRag.Embeddings;
using BookRag.Generation;
using BookRag.Ingestion;
using BookRag.Retrieval;
using BookRag.Storage;

// ── Initialise configuration ─────────────────────────────────────────────────
// All settings come from environment variables.  See .env.example for the
// full list of variables and their defaults.
var config = new AppConfig();

// ── Route to the correct sub-command ─────────────────────────────────────────
if (args.Length == 0)
{
    PrintUsage();
    return;
}

var command = args[0].ToLowerInvariant();

try
{
    switch (command)
    {
        case "ingest":
            await RunIngestAsync(args[1..], config);
            break;

        case "query":
            await RunQueryAsync(config);
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
        Console.WriteLine("Usage: bookrag ingest <file-or-url> [<file-or-url> ...]");
        Console.WriteLine("Examples:");
        Console.WriteLine("  bookrag ingest data/book.txt");
        Console.WriteLine("  bookrag ingest report.pdf");
        Console.WriteLine("  bookrag ingest https://example.com/article");
        return;
    }

    // Load the existing vector store so we can append to it.
    var store  = new JsonVectorStore(config.DataDirectory, config.CollectionName);
    var chunks = await store.LoadAsync();
    var nextId = chunks.Count > 0 ? chunks.Max(c => c.Id) + 1 : 0;

    Console.WriteLine($"Collection: '{config.CollectionName}' ({chunks.Count} existing chunks)");
    Console.WriteLine($"Store:      {((JsonVectorStore)store).FilePath}\n");

    using var embedder = new LocalEmbeddingService();
    var       newChunks = new List<BookRag.Models.Chunk>();

    foreach (var source in sources)
    {
        Console.Write($"Loading '{source}' ... ");

        try
        {
            var loader      = LoaderFactory.Create(source, config);
            var loaded      = await loader.LoadAsync(source);

            Console.WriteLine($"{loaded.Count} chunks extracted");
            Console.Write("  Embedding");

            foreach (var chunk in loaded)
            {
                chunk.Id = nextId++;
                chunk.Embedding = embedder.Embed(chunk.Text);
                Console.Write(".");
            }

            Console.WriteLine($" done");
            newChunks.AddRange(loaded);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n  [warn] Skipping '{source}': {ex.Message}");
            Console.ResetColor();
            // Continue with the next source rather than aborting the whole run.
        }
    }

    if (newChunks.Count == 0)
    {
        Console.WriteLine("\nNo new chunks were added.");
        return;
    }

    // Append and persist.
    chunks.AddRange(newChunks);
    await store.SaveAsync(chunks);

    Console.WriteLine($"\nSaved {newChunks.Count} new chunks.");
    Console.WriteLine($"Total chunks in '{config.CollectionName}': {chunks.Count}");
}

// ── Sub-command: query ────────────────────────────────────────────────────────

static async Task RunQueryAsync(AppConfig config)
{
    // API key is only required at query time (not for local ingestion).
    config.RequireApiKey();

    var store  = new JsonVectorStore(config.DataDirectory, config.CollectionName);
    var chunks = await store.LoadAsync();

    if (chunks.Count == 0)
    {
        Console.WriteLine($"No chunks found in collection '{config.CollectionName}'.");
        Console.WriteLine("Run 'bookrag ingest <source>' first.");
        return;
    }

    using var embedder = new LocalEmbeddingService();
    var retriever = new Retriever(chunks, embedder, config.SimilarityThreshold);
    var llm       = new GroqClient(config);
    var rag       = new RagAnswerer(retriever, llm, config.RetrievalTopK);

    Console.WriteLine($"Collection '{config.CollectionName}': {chunks.Count} chunks loaded.");
    Console.WriteLine($"Model: {config.GroqModel}  |  Top-K: {config.RetrievalTopK}  |  Threshold: {config.SimilarityThreshold}");
    Console.WriteLine("Type a question and press Enter. Leave blank to exit.\n");

    while (true)
    {
        Console.Write("Q> ");
        var question = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(question))
            break;

        var (answer, sources) = await rag.AskAsync(question);

        Console.WriteLine($"\nA> {answer}\n");

        if (sources.Count > 0)
        {
            Console.WriteLine("Sources:");
            foreach (var (chunk, score) in sources)
                Console.WriteLine($"  [{score:F3}] {chunk.ToCitationString()}");
        }
        else
        {
            Console.WriteLine("(No matching sources found above the similarity threshold)");
        }

        Console.WriteLine("\n" + new string('─', 60) + "\n");
    }
}

// ── Help ──────────────────────────────────────────────────────────────────────

static void PrintUsage()
{
    Console.WriteLine("""
        BookRag — Multipurpose RAG System

        Commands:
          ingest <source> [<source> ...]   Add documents to the knowledge base
          query                            Ask questions interactively

        Sources:
          path/to/file.txt                 Plain text file
          path/to/document.pdf             PDF file
          https://example.com/page         Web page URL

        Configuration (environment variables):
          GROQ_API_KEY       Required for query mode
          GROQ_MODEL         LLM model (default: llama-3.3-70b-versatile)
          COLLECTION_NAME    Knowledge base name (default: default)
          DATA_DIRECTORY     Where to store vectors (default: ./data)
          RETRIEVAL_TOP_K    Number of chunks to retrieve (default: 5)
          SIMILARITY_THRESHOLD  Min score 0-1 (default: 0.3)
          CHUNK_MAX_CHARS    Max chars per chunk (default: 600)

        Examples:
          bookrag ingest data/book.txt
          bookrag ingest report.pdf https://docs.example.com/api
          bookrag query
        """);
}
