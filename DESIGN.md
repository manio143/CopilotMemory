# copilot-memory — Local Memory for GitHub Copilot SDK (.NET)

**Goal:** A .NET library that gives Copilot SDK apps persistent, local-first semantic memory with LLM-powered fact extraction — no cloud dependencies beyond your Copilot subscription.

## How It Works

```
User sends prompt
    ↓
onUserPromptSubmitted hook
    ↓
embed(prompt) → DuckDB vector search → inject relevant memories as additionalContext
    ↓
Agent runs, produces response
    ↓
assistant.turn_end event
    ↓
Collect user + assistant messages for this turn
    ↓
Send to gpt-5-mini: extract facts (user-sourced + assistant-sourced separately)
    ↓
For each fact: embed → check similarity against existing memories
    ↓
If similarity > 0.95: send old + new to gpt-5-mini for UPDATE/DELETE/NONE decision
If similarity < 0.95: ADD as new memory
    ↓
Store in DuckDB with source tag ("user" | "assistant")
```

## Architecture

### Integration via Copilot SDK

```csharp
using CopilotMemory;
using GitHub.Copilot.SDK;

var memory = new MemoryService(new MemoryConfig
{
    DbPath = "~/.copilot-memory/memory.duckdb",
    ExtractionModel = "gpt-5-mini",
});

var client = new CopilotClient();
await client.StartAsync();

var session = await client.CreateSessionAsync(new SessionConfig
{
    Model = "gpt-5",
    Hooks = new SessionHooks
    {
        OnUserPromptSubmitted = memory.CreateRecallHook(),
    },
});

// Subscribe to events for capture
memory.AttachToSession(session);
```

### SDK Hooks & Events Used

| Hook/Event | Purpose |
|------------|---------|
| `onUserPromptSubmitted` | **Recall** — embed prompt, search, return `{ additionalContext }` |
| `user.message` event | Buffer user message for the current turn |
| `assistant.message` event | Buffer assistant response for the current turn |
| `assistant.turn_end` event | **Capture trigger** — process buffered turn through extraction |
| `session.start` / `session.resume` | Warm up embeddings model |
| `session.compaction_complete` | Log that context was compacted (memories become more important) |

### Core Components

```
CopilotMemory/
├── src/
│   ├── MemoryService.cs          # Public API: CreateRecallHook(), AttachToSession()
│   ├── MemoryConfig.cs           # Configuration
│   ├── Store/
│   │   ├── DuckDbMemoryStore.cs  # DuckDB vector store
│   │   ├── MemoryEntry.cs        # Id, Text, Source, Embedding, CreatedAt, UpdatedAt
│   │   └── SearchResult.cs       # Text, Score, Source, CreatedAt
│   ├── Embeddings/
│   │   └── LocalEmbedder.cs      # ElBruno.LocalEmbeddings wrapper
│   ├── Extraction/
│   │   ├── FactExtractor.cs      # LLM-based fact extraction (user + assistant)
│   │   ├── MemoryUpdater.cs      # LLM-based ADD/UPDATE/DELETE/NONE decisions
│   │   └── Prompts.cs            # Extraction & update prompt templates
│   ├── Recall/
│   │   ├── MemorySearch.cs       # Hybrid vector search
│   │   └── MemoryFormatter.cs    # Format memories for context injection
│   └── Hooks/
│       ├── RecallHook.cs         # onUserPromptSubmitted implementation
│       └── CaptureHandler.cs     # Event-based turn capture
├── CopilotMemory.csproj
└── tests/
    └── ...
```

### Dependencies

```xml
<PackageReference Include="GitHub.Copilot.SDK" Version="*" />
<PackageReference Include="ElBruno.LocalEmbeddings" Version="0.1.0-preview10148" />
<PackageReference Include="DuckDB.NET.Data.Full" Version="*" />
```

- **ElBruno.LocalEmbeddings** — ONNX Runtime + bge-micro-v2, auto-download from HuggingFace, ~50MB model
- **DuckDB.NET** — embedded analytical DB, single-file, FLOAT[384] arrays
- **Copilot SDK** — LLM access via `session.SendAsync()` for extraction

## Memory Store (DuckDB)

```sql
CREATE TABLE memories (
    id TEXT PRIMARY KEY,
    text TEXT NOT NULL,
    source TEXT NOT NULL DEFAULT 'user',   -- 'user' | 'assistant'
    embedding FLOAT[384] NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    session_id TEXT                         -- which session created this memory
);

-- Full-text search index
PRAGMA create_fts_index('memories', 'id', 'text');
```

### Concurrency

DuckDB is single-writer. For typical local dev usage (1-3 concurrent sessions):

```csharp
// File-lock based write serialization
private readonly SemaphoreSlim _writeLock = new(1, 1);

public async Task StoreAsync(MemoryEntry entry)
{
    await _writeLock.WaitAsync();
    try { /* insert */ }
    finally { _writeLock.Release(); }
}
```

Reads are concurrent (DuckDB allows multiple readers). This is sufficient for v1.

## Fact Extraction (LLM-Powered)

Based on mem0's proven prompts, adapted for our context. Two extraction passes per turn:

### 1. User Fact Extraction

Extracts facts from the **user's messages only** (assistant messages provided for context).

```
System prompt: USER_FACT_EXTRACTION_PROMPT
Input: "user: {user_message}\nassistant: {assistant_message}"
Output: {"facts": ["Prefers Vim for keyboard-driven workflow", "Working on openclaw-lite project"]}
```

**Prompt (adapted from mem0 `USER_MEMORY_EXTRACTION_PROMPT`):**

```
You are a Personal Information Organizer, specialized in accurately storing facts,
user memories, and preferences. Your primary role is to extract relevant pieces of
information from conversations and organize them into distinct, manageable facts.

IMPORTANT: Extract facts ONLY from the user's messages. The assistant's messages
are provided for context only — do not extract facts from them.

Types of Information to Remember:
1. Personal Preferences: likes, dislikes, tool choices, workflows
2. Personal Details: names, relationships, important dates
3. Plans and Intentions: upcoming events, goals, projects
4. Professional Details: job title, tech stack, career goals
5. Project Context: architectures, decisions, conventions
6. Technical Preferences: languages, frameworks, patterns, coding style

Few-shot examples:

User: I prefer using Vim because of its keyboard-driven workflow.
Assistant: That makes sense — Vim is great for efficiency once you learn the keybindings.
Output: {"facts": ["Prefers Vim for its keyboard-driven workflow"]}

User: We decided to use DuckDB for the vector store.
Assistant: Good choice, DuckDB handles analytical queries well.
Output: {"facts": ["Decided to use DuckDB for vector store"]}

User: Hi, can you help me with something?
Assistant: Of course! What do you need?
Output: {"facts": []}

Guidelines:
- Today's date is {date}.
- Return JSON with a "facts" key containing an array of strings.
- If no relevant facts found, return {"facts": []}.
- Detect the language of user input and record facts in the same language.
- Each fact should be a self-contained, atomic statement.
- Do not include information from assistant or system messages as facts.
```

### 2. Assistant Fact Extraction

Extracts facts from the **assistant's messages only** (user messages provided for context).

```
System prompt: ASSISTANT_FACT_EXTRACTION_PROMPT
Input: "user: {user_message}\nassistant: {assistant_message}"
Output: {"facts": ["Recommended DuckDB for analytical workloads"]}
```

**Prompt (adapted from mem0 `AGENT_MEMORY_EXTRACTION_PROMPT`):**

```
You are an Assistant Information Organizer, specialized in accurately storing facts
and knowledge produced by the AI assistant during conversations.

IMPORTANT: Extract facts ONLY from the assistant's messages. The user's messages
are provided for context only — do not extract facts from them.

Types of Information to Remember:
1. Technical Recommendations: tools, libraries, patterns suggested
2. Decisions Made: architectural choices, approaches selected
3. Knowledge Shared: explanations, corrections, clarifications
4. Project Understanding: what the assistant learned about the codebase

Few-shot examples:

User: Should I use SQLite or DuckDB?
Assistant: For vector similarity search with analytical queries, DuckDB is the better fit.
Output: {"facts": ["Recommended DuckDB for vector similarity and analytical queries"]}

User: What's the best way to handle concurrency?
Assistant: Use a SemaphoreSlim for write serialization — DuckDB is single-writer.
Output: {"facts": ["Advised SemaphoreSlim for DuckDB write serialization"]}

User: Fix this bug please.
Assistant: Done, the issue was a missing null check.
Output: {"facts": []}

Guidelines:
- Today's date is {date}.
- Return JSON with a "facts" key containing an array of strings.
- If no relevant facts found, return {"facts": []}.
- Each fact should be self-contained and atomic.
- Do not include information from user messages as facts.
```

### 3. Memory Update Decision

When a new fact has similarity >0.95 with an existing memory, the LLM decides what to do.

**Prompt (adapted from mem0 `DEFAULT_UPDATE_MEMORY_PROMPT`):**

```
You are a smart memory manager. Compare new facts against existing memories and
decide for each: ADD, UPDATE, DELETE, or NONE.

Rules:
- ADD: New information not present in memory. Generate a new ID.
- UPDATE: Information updates or enriches an existing memory. Keep the existing ID,
  update the text. Keep the version with MORE information.
- DELETE: New fact contradicts existing memory.
- NONE: Fact is already present, no change needed.

Current memory:
```
{existing_memories_json}
```

New facts:
```
{new_facts_json}
```

Return JSON only:
{
    "memory": [
        {
            "id": "<existing or new ID>",
            "text": "<memory content>",
            "event": "ADD|UPDATE|DELETE|NONE",
            "old_memory": "<old text, only if UPDATE>"
        }
    ]
}
```

## Recall

### Search

Hybrid search: vector similarity + optional FTS boost.

```csharp
public async Task<List<SearchResult>> SearchAsync(string query, int limit = 5, float minScore = 0.3f)
{
    var embedding = _embedder.Embed(query);
    
    // Vector similarity search
    var results = await _db.QueryAsync(@"
        SELECT id, text, source, created_at,
               array_cosine_similarity(embedding, $1::FLOAT[384]) AS score
        FROM memories
        WHERE array_cosine_similarity(embedding, $1::FLOAT[384]) > $2
        ORDER BY score DESC
        LIMIT $3",
        embedding, minScore, limit);
    
    return results.Select(r => new SearchResult
    {
        Text = r.Text,
        Score = r.Score,
        Source = r.Source,       // "user" or "assistant"
        CreatedAt = r.CreatedAt,
    }).ToList();
}
```

### Context Injection Format

```csharp
public string FormatMemories(List<SearchResult> memories)
{
    if (memories.Count == 0) return "";
    
    var lines = memories.Select(m =>
        $"- [{m.Source}] {m.Text}"
    );
    
    return string.Join("\n", new[]
    {
        "<relevant-memories>",
        "The following memories may be relevant. Source indicates origin (user/assistant).",
    }.Concat(lines).Append("</relevant-memories>"));
}
```

The `[user]`/`[assistant]` tag lets the consuming LLM weigh facts appropriately — user-stated facts are ground truth, assistant-stated facts are contextual.

### Recall Hook

```csharp
public Func<UserPromptSubmittedHookInput, HookInvocation, Task<UserPromptSubmittedHookOutput?>> CreateRecallHook()
{
    return async (input, invocation) =>
    {
        if (string.IsNullOrWhiteSpace(input.Prompt) || input.Prompt.Length < 5)
            return null;

        var results = await SearchAsync(input.Prompt);
        if (results.Count == 0) return null;

        return new UserPromptSubmittedHookOutput
        {
            AdditionalContext = FormatMemories(results),
        };
    };
}
```

## Capture Pipeline

```csharp
public class CaptureHandler
{
    private readonly List<string> _turnMessages = new();
    
    public void AttachToSession(CopilotSession session)
    {
        session.On(evt =>
        {
            switch (evt)
            {
                case UserMessageEvent msg:
                    _turnMessages.Add($"user: {msg.Data.Content}");
                    break;
                    
                case AssistantMessageEvent msg:
                    _turnMessages.Add($"assistant: {msg.Data.Content}");
                    break;
                    
                case AssistantTurnEndEvent:
                    _ = ProcessTurnAsync(); // fire-and-forget, don't block
                    break;
            }
        });
    }
    
    private async Task ProcessTurnAsync()
    {
        var conversation = string.Join("\n", _turnMessages);
        _turnMessages.Clear();
        
        // Extract facts from both perspectives (parallel)
        var userFactsTask = _extractor.ExtractUserFactsAsync(conversation);
        var assistantFactsTask = _extractor.ExtractAssistantFactsAsync(conversation);
        
        await Task.WhenAll(userFactsTask, assistantFactsTask);
        
        var userFacts = userFactsTask.Result;       // tagged source = "user"
        var assistantFacts = assistantFactsTask.Result; // tagged source = "assistant"
        
        foreach (var fact in userFacts.Concat(assistantFacts))
        {
            var embedding = _embedder.Embed(fact.Text);
            
            // Check for similar existing memories
            var similar = await _store.FindSimilarAsync(embedding, threshold: 0.95f);
            
            if (similar.Count > 0)
            {
                // LLM decides: UPDATE, DELETE, or NONE
                var decision = await _updater.DecideAsync(similar, fact);
                await _store.ApplyDecisionAsync(decision);
            }
            else
            {
                // New memory — ADD directly
                await _store.AddAsync(new MemoryEntry
                {
                    Id = Guid.NewGuid().ToString(),
                    Text = fact.Text,
                    Source = fact.Source,
                    Embedding = embedding,
                });
            }
        }
    }
}
```

### LLM Access for Extraction

Use the Copilot SDK itself — create a lightweight extraction session with gpt-5-mini:

```csharp
public class FactExtractor
{
    private readonly CopilotClient _client;
    private CopilotSession? _extractionSession;
    
    public async Task<List<Fact>> ExtractUserFactsAsync(string conversation)
    {
        _extractionSession ??= await _client.CreateSessionAsync(new SessionConfig
        {
            Model = "gpt-5-mini",
            SystemMessage = Prompts.UserFactExtraction(DateTime.UtcNow),
        });
        
        var response = await SendAndCollectAsync(_extractionSession, $"Input:\n{conversation}");
        return ParseFacts(response, source: "user");
    }
}
```

## Embeddings (ElBruno.LocalEmbeddings)

```csharp
using ElBruno.LocalEmbeddings;

public class LocalEmbeddingService : IDisposable
{
    private readonly LocalEmbedder _embedder;
    
    public LocalEmbeddingService()
    {
        _embedder = new LocalEmbedder(); // loads ONNX model (~50MB on first use)
    }
    
    public float[] Embed(string text)
    {
        var embedding = _embedder.Embed(text);
        return embedding.Values.ToArray(); // FLOAT[384]
    }
    
    public float Similarity(float[] a, float[] b)
    {
        return LocalEmbedder.Similarity(
            new EmbeddingF32(a),
            new EmbeddingF32(b));
    }
    
    public void Dispose() => _embedder.Dispose();
}
```

## Public API

```csharp
// Core service
var memory = new MemoryService(config);

// Hook integration
session.Hooks.OnUserPromptSubmitted = memory.CreateRecallHook();
memory.AttachToSession(session);

// Explicit operations
await memory.StoreAsync("User prefers Vim", source: "user");
var results = await memory.SearchAsync("editor preferences");
await memory.ForgetAsync("old project info");
var stats = await memory.GetStatsAsync(); // count, size, last updated

// On-demand extraction (useful for importing old conversations)
var facts = await memory.ExtractFactsAsync(messages);
```

## Package Shape

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <PackageId>CopilotMemory</PackageId>
    <Version>0.1.0</Version>
    <Description>Local-first semantic memory for GitHub Copilot SDK</Description>
  </PropertyGroup>
  
  <ItemGroup>
    <PackageReference Include="GitHub.Copilot.SDK" Version="*" />
    <PackageReference Include="ElBruno.LocalEmbeddings" Version="0.1.0-preview10148" />
    <PackageReference Include="DuckDB.NET.Data.Full" Version="*" />
  </ItemGroup>
</Project>
```

## Key Design Decisions

1. **LLM extraction over rule-based** — aligned with mem0. gpt-5-mini is cheap and produces higher-quality atomic facts than regex triggers
2. **Two-pass extraction** (user + assistant separately) — each fact tagged with `source`, returned during recall for LLM weighting. No filtering by source during search.
3. **Similarity gate at 0.95** — only triggers LLM update check for near-duplicates. Below 0.95 = new memory, above = potential conflict needing LLM resolution
4. **ElBruno.LocalEmbeddings** — local ONNX, no API key, ~50MB model, <50ms inference
5. **DuckDB** — single-file, embedded, native FLOAT[384] + cosine similarity. File-lock for write concurrency.
6. **Copilot SDK as LLM provider** — no separate API key. Extraction uses gpt-5-mini via a dedicated session.

## MVP Scope (v0.1)

- [x] DuckDB memory store with FLOAT[384] embeddings
- [x] Local embeddings via ElBruno.LocalEmbeddings (switched from SmartComponents — single model limitation)
- [x] LLM fact extraction (user + assistant, via Copilot SDK)
- [x] LLM memory update decisions (ADD/UPDATE/DELETE/NONE)
- [x] Recall hook: `onUserPromptSubmitted` → search → `additionalContext`
- [x] Capture handler: `user.message` + `assistant.message` + `assistant.turn_end`
- [x] Source tagging ("user" / "assistant") on every memory
- [x] Explicit API: `StoreAsync`, `SearchAsync`, `ForgetAsync`

## Future (v0.2+)

- Custom tools: `memory_store`, `memory_search` registered as Copilot tools
- Memory aging / decay (reduce score for old, unaccessed memories)
- Import/export (JSONL)
- Session-scoped ephemeral memory
- Configurable extraction prompts
- `Microsoft.Extensions.AI` abstraction for embeddings (swap providers)
- Optional FTS boost on vector search
