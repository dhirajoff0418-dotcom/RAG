# BookRag — Ask Questions About Any Document

BookRag is a Retrieval-Augmented Generation (RAG) system built in C#.  
You give it documents or web pages and ask questions in plain English.  
It finds the relevant passages and uses an LLM to generate a grounded answer with citations.

**Supported sources:** `.txt` files · `.pdf` files · any website URL

---

## How It Works

```
Ingest:
  Document → split into chunks → prefix each chunk with [Page N | CHAPTER] →
  embed with all-MiniLM-L6-v2 → save to JSON vector store

Query:
  Question
    → [LLM] rewrite into 3 search queries        (QUERY_REWRITE=true)
    → [Vector] cosine similarity search × 4 queries
    → [BM25]   keyword search        × 4 queries
    → Reciprocal Rank Fusion (merge all results)
    → [LLM] rerank top-20 candidates 0–10        (RERANK=true)
    → take top-6 chunks as context
    → [LLM] generate final answer
```

---

## Prerequisites

| Requirement | Notes |
|-------------|-------|
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | Check with `dotnet --version` |
| [Groq API key](https://console.groq.com) | Free account, no credit card needed |

---

## Setup

```powershell
# Set your API key (Windows PowerShell)
$env:GROQ_API_KEY = "your_key_here"

# Change to the project folder
cd src/BookRag
```

---

## Ingest a Document

```powershell
# Fresh ingest (clears any existing index)
dotnet run -- ingest --fresh ..\..\data\harry-potter-and-the-philosophers-stone-by-jk-rowling.pdf

# Append to an existing collection
dotnet run -- ingest additional-book.pdf

# Multiple sources at once
dotnet run -- ingest --fresh book.pdf https://example.com/article
```

> **Important:** Always use `--fresh` after changing chunking settings
> (`CHUNK_MAX_CHARS`, `CHUNK_OVERLAP_SENTENCES`) to avoid mixing old and
> new chunk formats in the same index.

---

## Ask Questions

```powershell
dotnet run -- query
```

```
Q> What are the names of Harry's parents?

A> From the book:
   Harry Potter's parents were Lily and James Potter. They were wizards who
   were killed by Lord Voldemort when Harry was just a baby.

   Interpretation:
   Their deaths define the entire story — Harry's survival is the central
   mystery, and the love his mother gave him is later revealed to be his
   greatest protection.

Sources:
  [9] harry-potter-and-the-philosophers-stone-by-jk-rowling.pdf, page 16
  [8] harry-potter-and-the-philosophers-stone-by-jk-rowling.pdf, page 46
```

### Debug mode

Prints rewritten queries, vector hits, BM25 hits, fused ranking, and reranker scores:

```powershell
dotnet run -- query --debug
```

Or set `DEBUG=true` to enable it permanently.

---

## Evaluate Retrieval Quality

```powershell
dotnet run -- eval
# or specify a custom eval file:
dotnet run -- eval ..\..\data\eval_hp1.json
```

Output:
```
[1/10] What are the names of Harry Potter's parents?
  Retrieval [✓]  Answer [✓]
  Expected keywords: James, Lily, Potter

RETRIEVAL HIT RATE: 9/10 (90%)
ANSWER   HIT RATE: 8/10 (80%)
```

A sample 10-question eval set for Harry Potter Book 1 is included at `data/eval_hp1.json`.

---

## Configuration

All settings are environment variables. No code changes needed.

| Variable | Default | Description |
|----------|---------|-------------|
| `GROQ_API_KEY` | *(required)* | Your Groq API key |
| `GROQ_MODEL` | `openai/gpt-oss-120b` | LLM for queries, rewriting, reranking |
| `LLM_TEMPERATURE` | `0.1` | LLM temperature (0 = factual) |
| `COLLECTION_NAME` | `default` | Active knowledge-base name |
| `DATA_DIRECTORY` | `./data` | Where vectors are stored |
| `RETRIEVAL_TOP_K` | `6` | Final chunks sent to the LLM |
| `SIMILARITY_THRESHOLD` | `0.15` | Min cosine score for vector filter |
| `RETRIEVAL_CANDIDATE_K` | `20` | Candidates per search pass (vector+BM25) |
| `BM25_K1` | `1.5` | BM25 term-frequency saturation |
| `BM25_B` | `0.75` | BM25 length normalisation |
| `QUERY_REWRITE` | `true` | LLM query expansion before retrieval |
| `RERANK` | `true` | LLM reranking of top-20 candidates |
| `RERANK_CANDIDATE_K` | `20` | Candidates sent to the reranker |
| `CHUNK_MAX_CHARS` | `500` | Max chars per chunk body |
| `CHUNK_OVERLAP_SENTENCES` | `2` | Sentences of overlap between chunks |
| `EMBEDDING_MODEL` | `all-MiniLM-L6-v2` | Embedding model identifier |
| `DEBUG` | `false` | Always-on debug output |

See `.env.example` for the full list with descriptions.

---

## Multiple Knowledge Bases

```powershell
$env:COLLECTION_NAME = "research"
dotnet run -- ingest --fresh paper.pdf

$env:COLLECTION_NAME = "research"
dotnet run -- query
```

---

## Project Structure

```
BookRag/
├── data/
│   ├── eval_hp1.json            ← 10 sample evaluation questions (HP Book 1)
│   └── *.pdf / *.txt            ← Your documents
├── src/BookRag/
│   ├── Config/
│   │   └── AppConfig.cs         ← All settings from env vars
│   ├── Embeddings/
│   │   ├── IEmbeddingService.cs ← Interface (swappable)
│   │   └── LocalEmbeddingService.cs ← all-MiniLM-L6-v2 via ONNX
│   ├── Generation/
│   │   ├── GroqClient.cs        ← Groq API client
│   │   ├── ILlmClient.cs        ← Interface (swappable)
│   │   ├── PromptTemplates.cs   ← All prompts in one place
│   │   ├── QueryRewriter.cs     ← LLM query expansion (3 rewrites)
│   │   ├── RagAnswerer.cs       ← Full pipeline orchestrator
│   │   └── Reranker.cs          ← LLM relevance scoring (0–10)
│   ├── Ingestion/
│   │   ├── Chunker.cs           ← Sentence-based chunking + page prefix
│   │   ├── IDocumentLoader.cs
│   │   ├── LoaderFactory.cs
│   │   ├── PdfLoader.cs
│   │   ├── TextCleaner.cs
│   │   ├── TxtLoader.cs
│   │   └── WebLoader.cs
│   ├── Models/
│   │   └── Chunk.cs             ← Data model (includes EmbeddingModel field)
│   ├── Retrieval/
│   │   ├── Bm25Index.cs         ← Pure C# BM25 keyword index
│   │   ├── DebugInfo.cs         ← Diagnostic data bag
│   │   ├── Retriever.cs         ← Hybrid search (vector + BM25 + RRF)
│   │   └── RrfMerger.cs         ← Reciprocal Rank Fusion
│   ├── Storage/
│   │   ├── IVectorStore.cs
│   │   └── JsonVectorStore.cs   ← JSON persistence + meta sidecar
│   └── Program.cs               ← ingest / query / eval commands
├── .env.example                 ← All env vars documented
└── README.md
```

---

## Common Questions

**Why is retrieval slow the first time?**  
The ONNX embedding model loads on first use. Subsequent queries in the same session are fast.

**The PDF says "No text could be extracted"**  
The PDF is scanned (image-only). OCR is not supported — use a PDF where you can select text.

**I get "embedding model mismatch"**  
Your index was built with a different `EMBEDDING_MODEL`. Re-ingest with `--fresh` or restore the original `EMBEDDING_MODEL` env var.

**How do I disable query rewriting or reranking?**  
Set `QUERY_REWRITE=false` or `RERANK=false`. Both reduce LLM calls and latency at some cost to accuracy.

**Can I use a different LLM?**  
Implement `ILlmClient` and pass your instance to `GroqClient`'s constructor-equivalent.

---

## Tech Stack

- **Language:** C# / .NET 10
- **Embeddings:** [SmartComponents LocalEmbeddings](https://github.com/dotnet-smartcomponents/smartcomponents) — all-MiniLM-L6-v2 (runs locally, no API key)
- **Keyword search:** Pure C# BM25 implementation
- **PDF extraction:** [PdfPig](https://github.com/UglyToad/PdfPig)
- **HTML parsing:** [HtmlAgilityPack](https://html-agility-pack.net/)
- **LLM:** [Groq](https://groq.com/) (OpenAI-compatible API)
- **Vector store:** JSON file (no external database required)
