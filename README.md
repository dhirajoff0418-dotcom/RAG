# BookRag — Ask Questions About Any Document

BookRag is a Retrieval-Augmented Generation (RAG) system built in C#.  
You give it documents or web pages, and you can ask questions about them in plain English.  
It finds the relevant passages and uses an LLM to generate a grounded answer with citations.

**Supported sources:** `.txt` files · `.pdf` files · any website URL

---

## How It Works (in plain terms)

```
Your document  →  split into chunks  →  embed each chunk as a vector  →  saved to disk

Your question  →  embed question  →  find similar chunks  →  send to LLM  →  answer + sources
```

No external vector database required. Everything runs locally except the LLM call (Groq).

---

## Prerequisites

| Requirement | Notes |
|-------------|-------|
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | Check with `dotnet --version` |
| [Groq API key](https://console.groq.com) | Free account, no credit card needed |

---

## Setup (5 minutes)

**1. Clone the repo**
```
git clone <repo-url>
cd BookRag
```

**2. Set your Groq API key**

On Windows PowerShell:
```powershell
$env:GROQ_API_KEY = "your_key_here"
```

On Linux / macOS:
```bash
export GROQ_API_KEY="your_key_here"
```

> You only need this for the `query` step. Ingestion works without it.

**3. Go to the project folder**
```
cd src/BookRag
```

That's all the setup you need.

---

## Step 1 — Ingest Your Documents

Point the ingester at any file or URL. It reads the content, splits it into chunks, embeds them, and saves them to `data/default.json`.

```powershell
# A text file
dotnet run -- ingest ../../data/book.txt

# A PDF file
dotnet run -- ingest path/to/your/document.pdf

# A web page
dotnet run -- ingest https://en.wikipedia.org/wiki/Retrieval-augmented_generation

# Multiple sources at once
dotnet run -- ingest ../../data/book.txt report.pdf https://example.com/article
```

You will see output like:
```
Collection: 'default' (0 existing chunks)
Loading 'book.txt' ... 10 chunks extracted
  Embedding.......... done
Saved 10 new chunks.
```

Each ingest call **adds** to the existing collection, so you can build up a knowledge base incrementally.

---

## Step 2 — Ask Questions

```powershell
dotnet run -- query
```

Type a question and press Enter:

```
Q> Who is Mira Castellan?

A> Mira Castellan is the new keeper of the Merrow Bay lighthouse. She arrived
   on a grey Tuesday in October, carrying a brass compass that belonged to her
   grandfather.

Sources:
  [0.821] book.txt — CHAPTER 1: THE ARRIVAL
  [0.743] book.txt — CHAPTER 2: THE VILLAGE

────────────────────────────────────────────────

Q>
```

Leave the question blank and press Enter to exit.

---

## Multiple Knowledge Bases

Use `COLLECTION_NAME` to keep separate knowledge bases that do not mix.

```powershell
# Ingest into a "research" collection
$env:COLLECTION_NAME = "research"
dotnet run -- ingest paper.pdf

# Query only that collection
$env:COLLECTION_NAME = "research"
dotnet run -- query
```

Each collection is a separate JSON file in the `data/` folder.

---

## Configuration

You do not need to change any code to configure the system. Set environment variables:

| Variable | Default | What it controls |
|----------|---------|-----------------|
| `GROQ_API_KEY` | *(required for query)* | Your Groq API key |
| `GROQ_MODEL` | `llama-3.3-70b-versatile` | Which LLM to use |
| `COLLECTION_NAME` | `default` | Active knowledge base |
| `DATA_DIRECTORY` | `./data` | Where vectors are stored |
| `RETRIEVAL_TOP_K` | `5` | Chunks retrieved per query |
| `SIMILARITY_THRESHOLD` | `0.3` | Minimum relevance score (0–1) |
| `CHUNK_MAX_CHARS` | `600` | Max characters per chunk |

Copy `.env.example` to `.env` to see all options with descriptions.

---

## Project Structure

```
BookRag/
├── data/                        ← Put your documents here
│   └── book.txt                 ← Sample document included
├── src/BookRag/
│   ├── Config/                  ← All configuration in one place
│   ├── Embeddings/              ← Local embedding model (no API key needed)
│   ├── Generation/              ← LLM client + prompt templates
│   ├── Ingestion/               ← TXT, PDF, and web loaders
│   ├── Models/                  ← Chunk data model
│   ├── Retrieval/               ← Semantic similarity search
│   ├── Storage/                 ← Saves/loads the vector index
│   └── Program.cs               ← Entry point (ingest / query commands)
├── .env.example                 ← All environment variables documented
└── README.md
```

---

## Common Questions

**The PDF says "No text could be extracted"**  
The PDF is likely scanned (image only). OCR is not currently supported. Use a PDF where you can select text with your mouse.

**I get "GROQ_API_KEY is not set"**  
You only need this for `query`, not for `ingest`. Set it as shown in the Setup section.

**I want to start fresh and clear the knowledge base**  
Delete `data/default.json` (or whatever your `COLLECTION_NAME` is).

**Can I use a different LLM?**  
Set `GROQ_MODEL` to any model available on your Groq account. The architecture supports adding other providers (OpenAI, Ollama) by implementing the `ILlmClient` interface.

---

## Tech Stack

- **Language:** C# / .NET 10
- **Embeddings:** [SmartComponents LocalEmbeddings](https://github.com/dotnet-smartcomponents/smartcomponents) (all-MiniLM-L6-v2, runs locally)
- **PDF extraction:** [PdfPig](https://github.com/UglyToad/PdfPig)
- **HTML parsing:** [HtmlAgilityPack](https://html-agility-pack.net/)
- **LLM:** [Groq](https://groq.com/) (OpenAI-compatible API)
- **Vector store:** JSON file (simple, no external database)
