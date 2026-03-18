using System.Text.Json;
using CopilotMemory.Embeddings;
using CopilotMemory.Extraction;
using CopilotMemory.Recall;
using CopilotMemory.Store;

namespace CopilotMemory;

/// <summary>
/// Events emitted by the memory pipeline for observability.
/// </summary>
public record MemoryPipelineEvent
{
    /// <summary>The pipeline step that emitted this event.</summary>
    public required string Step { get; init; }
    
    /// <summary>Human-readable description of what happened.</summary>
    public required string Detail { get; init; }
    
    /// <summary>Duration of the step in milliseconds (0 if not measured).</summary>
    public long DurationMs { get; init; }
}

/// <summary>
/// Orchestrates the complete memory pipeline: fact extraction, embedding, deduplication,
/// and storage. This is the main entry point for memory operations.
/// </summary>
public class MemoryPipeline : IDisposable
{
    private readonly DuckDbMemoryStore _store;
    private readonly LocalEmbeddingService _embedder;
    private readonly FactExtractor _extractor;
    private readonly MemoryUpdater _updater;
    private readonly float _dedupThreshold;
    private Action<MemoryPipelineEvent>? _onEvent;

    /// <summary>
    /// Creates a new memory pipeline with the specified components.
    /// </summary>
    /// <param name="store">DuckDB memory store for persistence.</param>
    /// <param name="embedder">Local embedding service for vector generation.</param>
    /// <param name="extractor">Fact extractor using LLM.</param>
    /// <param name="updater">Memory updater for deduplication decisions.</param>
    /// <param name="dedupThreshold">Cosine similarity threshold for deduplication (default: 0.7).</param>
    public MemoryPipeline(
        DuckDbMemoryStore store,
        LocalEmbeddingService embedder,
        FactExtractor extractor,
        MemoryUpdater updater,
        float dedupThreshold = 0.7f)
    {
        _store = store;
        _embedder = embedder;
        _extractor = extractor;
        _updater = updater;
        _dedupThreshold = dedupThreshold;
    }

    /// <summary>
    /// Subscribe to pipeline events. Returns a disposable to unsubscribe.
    /// </summary>
    /// <param name="handler">Event handler to be invoked for each pipeline event.</param>
    /// <returns>IDisposable that unsubscribes when disposed.</returns>
    public IDisposable On(Action<MemoryPipelineEvent> handler)
    {
        _onEvent += handler;
        return new Unsubscriber(() => _onEvent -= handler);
    }

    /// <summary>
    /// Processes a conversation turn: extracts facts, embeds them, checks for duplicates,
    /// and stores new or updated memories.
    /// </summary>
    /// <param name="userMessage">The user's message in this turn.</param>
    /// <param name="assistantMessage">The assistant's response in this turn.</param>
    public async Task ProcessTurnAsync(string userMessage, string assistantMessage)
    {
        var conversation = $"user: {userMessage}\nassistant: {assistantMessage}";

        // Step 1: Extract facts (parallel)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var userFactsTask = _extractor.ExtractUserFactsAsync(conversation);
        var assistantFactsTask = _extractor.ExtractAssistantFactsAsync(conversation);
        await Task.WhenAll(userFactsTask, assistantFactsTask);
        sw.Stop();

        var userFacts = userFactsTask.Result;
        var assistantFacts = assistantFactsTask.Result;
        var allFacts = userFacts.Concat(assistantFacts).ToList();

        Emit("extract", $"Extracted {allFacts.Count} facts ({userFacts.Count} user, {assistantFacts.Count} assistant)", sw.ElapsedMilliseconds);

        if (allFacts.Count == 0) return;

        // Step 2: Embed all facts
        sw.Restart();
        var factEmbeddings = allFacts.Select(f => (Fact: f, Embedding: _embedder.Embed(f.Text))).ToList();
        sw.Stop();
        Emit("embed", $"Embedded {factEmbeddings.Count} facts", sw.ElapsedMilliseconds);

        // Step 3: Collect dedup candidates (batch)
        sw.Restart();
        var factsWithSimilar = factEmbeddings.Select(fe =>
        {
            var similar = _store.FindSimilar(fe.Embedding, _dedupThreshold);
            return (fe.Fact, fe.Embedding, Similar: similar);
        }).ToList();
        sw.Stop();

        var factsNeedingUpdate = factsWithSimilar.Where(f => f.Similar.Count > 0).ToList();
        var newFacts = factsWithSimilar.Where(f => f.Similar.Count == 0).ToList();
        Emit("dedup_search", $"{factsNeedingUpdate.Count} facts have similar matches, {newFacts.Count} are new", sw.ElapsedMilliseconds);

        // Step 4: Batch LLM update decision for all facts with similar matches
        if (factsNeedingUpdate.Count > 0)
        {
            // Collect all unique existing memories and all new fact texts
            var allExisting = factsNeedingUpdate
                .SelectMany(f => f.Similar)
                .DistinctBy(s => s.Id)
                .Select(s => (s.Id, s.Text))
                .ToList();

            var allNewTexts = factsNeedingUpdate.Select(f => f.Fact.Text).ToList();

            sw.Restart();
            var decisions = await _updater.DecideAsync(allExisting, allNewTexts);
            sw.Stop();

            Emit("update_decision", $"LLM returned {decisions.Count} decisions: " +
                string.Join(", ", decisions.Select(d => $"{d.Event}")), sw.ElapsedMilliseconds);

            foreach (var decision in decisions)
            {
                switch (decision.Event)
                {
                    case UpdateEvent.UPDATE:
                        var updatedEmbedding = _embedder.Embed(decision.Text);
                        await _store.UpdateAsync(decision.Id, decision.Text, updatedEmbedding);
                        Emit("update", $"Updated '{decision.Id}': {decision.Text}");
                        break;
                    case UpdateEvent.DELETE:
                        await _store.DeleteAsync(decision.Id);
                        Emit("delete", $"Deleted '{decision.Id}'");
                        break;
                    case UpdateEvent.ADD:
                        // Find source from the matching new fact
                        var source = factsNeedingUpdate
                            .FirstOrDefault(f => f.Fact.Text == decision.Text).Fact?.Source ?? "user";
                        await _store.AddAsync(new MemoryEntry
                        {
                            Id = decision.Id,
                            Text = decision.Text,
                            Source = source,
                            Embedding = _embedder.Embed(decision.Text),
                        });
                        Emit("add", $"Added '{decision.Id}': {decision.Text} (source={source})");
                        break;
                    // NONE: do nothing
                }
            }
        }

        // Step 5: Store new facts directly
        foreach (var (fact, embedding, _) in newFacts)
        {
            var id = Guid.NewGuid().ToString();
            await _store.AddAsync(new MemoryEntry
            {
                Id = id,
                Text = fact.Text,
                Source = fact.Source,
                Embedding = embedding,
            });
            Emit("add_new", $"Stored '{fact.Text}' (source={fact.Source})");
        }
    }

    /// <summary>
    /// Recalls memories relevant to a query using semantic similarity search.
    /// </summary>
    /// <param name="query">The search query text.</param>
    /// <param name="limit">Maximum number of results to return (default: 5).</param>
    /// <param name="minScore">Minimum similarity score threshold (default: 0.65).</param>
    /// <returns>List of matching search results, ordered by similarity score.</returns>
    public List<SearchResult> Recall(string query, int limit = 5, float minScore = 0.65f)
    {
        var embedding = _embedder.Embed(query);
        return _store.Search(embedding, limit, minScore);
    }

    /// <summary>
    /// Recalls memories and formats them for LLM context injection.
    /// </summary>
    /// <param name="query">The search query text.</param>
    /// <param name="limit">Maximum number of results to return (default: 5).</param>
    /// <returns>Formatted context string suitable for LLM prompts.</returns>
    public string RecallFormatted(string query, int limit = 5)
    {
        var results = Recall(query, limit);
        return MemoryRecaller.FormatMemories(results);
    }

    /// <summary>
    /// Explicitly stores a fact in memory (used by memory_store tool).
    /// </summary>
    /// <param name="text">The fact text to store.</param>
    /// <param name="source">Source of the fact: "user" or "assistant" (default: "user").</param>
    public async Task StoreAsync(string text, string source = "user")
    {
        var embedding = _embedder.Embed(text);
        await _store.AddAsync(new MemoryEntry
        {
            Id = Guid.NewGuid().ToString(),
            Text = text,
            Source = source,
            Embedding = embedding,
        });
        Emit("store_explicit", $"Stored '{text}' (source={source})");
    }

    /// <summary>
    /// Deletes memories matching a query (used by memory_forget tool).
    /// </summary>
    /// <param name="query">Query to match memories for deletion.</param>
    /// <param name="minScore">Minimum similarity score for matches (default: 0.85).</param>
    /// <returns>Number of memories deleted.</returns>
    public async Task<int> ForgetAsync(string query, float minScore = 0.85f)
    {
        var results = Recall(query, limit: 10, minScore: minScore);
        foreach (var r in results)
            await _store.DeleteAsync(r.Id);
        Emit("forget", $"Deleted {results.Count} memories matching '{query}'");
        return results.Count;
    }

    /// <summary>
    /// Warms up the embedding model by running a test embedding.
    /// Should be called during session initialization to avoid first-use latency.
    /// </summary>
    public void WarmUp()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _embedder.Embed("warmup");
        sw.Stop();
        Emit("warmup", $"Embedding model loaded", sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Gets all memories from the store (without embeddings for efficiency).
    /// </summary>
    /// <returns>List of all memory entries.</returns>
    public List<MemoryEntry> GetAllMemories() => _store.GetAll();
    
    /// <summary>
    /// Gets statistics about the memory store (count, oldest, newest).
    /// </summary>
    /// <returns>Memory statistics.</returns>
    public MemoryStats GetStats() => _store.GetStats();

    private void Emit(string step, string detail, long durationMs = 0) =>
        _onEvent?.Invoke(new MemoryPipelineEvent { Step = step, Detail = detail, DurationMs = durationMs });

    /// <summary>
    /// Disposes the pipeline and its components (store and embedder).
    /// </summary>
    public void Dispose()
    {
        _store.Dispose();
        _embedder.Dispose();
    }

    private sealed class Unsubscriber(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
