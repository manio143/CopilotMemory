# CopilotMemory

**Local-first semantic memory library for GitHub Copilot SDK (.NET)**

CopilotMemory gives your GitHub Copilot agent a persistent, semantic memory system powered by LLM fact extraction, local embeddings, and vector similarity search — all stored locally in DuckDB.

## Features

- 🧠 **LLM-powered fact extraction** — Automatically extracts relevant facts from conversations using `gpt-5-mini`
- 🔍 **Semantic search** — Vector similarity search with local embeddings (bge-micro-v2, 384 dimensions)
- 💾 **DuckDB vector store** — Embedded analytical database with native FLOAT array support and cosine similarity
- 🔄 **Smart deduplication** — LLM-based update decisions (ADD/UPDATE/DELETE/NONE) to prevent redundant memories
- 🪝 **SDK hooks** — Seamless integration with GitHub Copilot SDK via `SessionHooks` for auto-recall and auto-capture
- 🛠️ **AI tools** — `memory_store`, `memory_recall`, and `memory_forget` tools for explicit memory operations
- ✅ **Battle-tested** — 12 integration test scenarios covering extraction, recall, dedup, and edge cases

## Requirements

- **.NET 10** or later
- **GitHub Copilot subscription** (for LLM calls via Copilot SDK)

## Quick Start

### 1. Initialize the memory pipeline

```csharp
using CopilotMemory;
using CopilotMemory.Extraction;
using CopilotMemory.Embeddings;
using CopilotMemory.Store;
using GitHub.Copilot.SDK;

// Create components
var store = new DuckDbMemoryStore("memory.duckdb");
var embedder = new LocalEmbeddingService(); // Uses bge-micro-v2 by default
var llmClient = new SdkLlmClient("gpt-5-mini");
var extractor = new FactExtractor(llmClient);
var updater = new MemoryUpdater(llmClient);

// Create pipeline
var pipeline = new MemoryPipeline(store, embedder, extractor, updater);

// Optional: Subscribe to pipeline events for observability
pipeline.On(evt => Console.WriteLine($"[{evt.Step}] {evt.Detail} ({evt.DurationMs}ms)"));
```

### 2. Integrate with Copilot SDK

```csharp
// Create Copilot client
var client = new CopilotClient();
await client.StartAsync();

// Wire up memory hooks
var hooks = new CopilotMemoryHooks(pipeline);

// Create session with memory recall
var session = await client.CreateSessionAsync(new SessionConfig
{
    Model = "gpt-5-turbo",
    Hooks = hooks.CreateHooks(), // Enables auto-recall before each turn
});

// Attach memory capture (extracts facts after each assistant response)
hooks.AttachCapture(session);

// Add memory tools (optional: for explicit memory operations)
var tools = new CopilotMemoryTools(pipeline);
session.UpdateConfig(new SessionConfigUpdate
{
    Tools = tools.All, // Adds memory_store, memory_recall, memory_forget
});

// Use the session normally
var response = await session.SendAndWaitAsync("What do you remember about my coding preferences?");
```

## How It Works

### Auto-Recall (OnUserPromptSubmitted hook)
Before each user prompt, the pipeline:
1. Embeds the user's query
2. Searches the vector store for relevant memories (cosine similarity > 0.65)
3. Injects formatted memories into the LLM context via `AdditionalContext`

### Auto-Capture (AttachCapture)
After each assistant response, the pipeline:
1. Extracts facts from the conversation using LLM prompts (separate for user/assistant facts)
2. Embeds all extracted facts
3. Searches for similar existing memories (cosine similarity > 0.7)
4. Asks the LLM to decide: ADD (new), UPDATE (enrich), DELETE (contradict), or NONE (duplicate)
5. Applies decisions to the DuckDB store

### Deduplication Strategy
- **Similarity threshold:** 0.7 cosine similarity triggers dedup check
- **LLM-based decisions:** The `MemoryUpdater` asks the LLM to compare new facts with existing memories
- **Update vs Add:** If a fact enriches or supersedes an existing memory, it UPDATEs the old one (preserving the ID)
- **Delete:** Only if the new fact explicitly contradicts an existing memory

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                      CopilotMemory Pipeline                      │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌──────────────┐    ┌───────────────┐    ┌──────────────┐     │
│  │FactExtractor │───▶│MemoryUpdater  │───▶│  DuckDB      │     │
│  │ (LLM)        │    │ (LLM)         │    │  Store       │     │
│  └──────────────┘    └───────────────┘    └──────────────┘     │
│         │                    │                     ▲             │
│         │                    │                     │             │
│         ▼                    ▼                     │             │
│  ┌──────────────────────────────────────┐         │             │
│  │  LocalEmbeddingService               │─────────┘             │
│  │  (bge-micro-v2, 384 dims)            │                       │
│  └──────────────────────────────────────┘                       │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘
         ▲                                      ▲
         │                                      │
    ┌────┴────────┐                  ┌─────────┴────────┐
    │ CopilotMemoryHooks │          │ CopilotMemoryTools│
    │ (auto-recall/capture)│        │ (explicit tools)   │
    └─────────────────────┘          └───────────────────┘
```

**Key Components:**
- **MemoryPipeline** — Orchestrates extraction, embedding, dedup, and storage
- **CopilotMemoryHooks** — SDK hooks for auto-recall and auto-capture
- **CopilotMemoryTools** — AI tools (`memory_store`, `memory_recall`, `memory_forget`)
- **LocalEmbeddingService** — ONNX Runtime embeddings (bge-micro-v2 via ElBruno.LocalEmbeddings)
- **DuckDbMemoryStore** — Vector store with cosine similarity (FLOAT[384] arrays)
- **FactExtractor** — Extracts facts from conversations using LLM prompts
- **MemoryUpdater** — Decides ADD/UPDATE/DELETE/NONE for deduplication

## Documentation

For detailed design decisions, prompt engineering, and architectural rationale, see [DESIGN.md](DESIGN.md).

## License

MIT License — see [LICENSE](LICENSE) for details.

Copyright (c) 2026 Marian (manio143)
