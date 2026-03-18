using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace CopilotMemory;

/// <summary>
/// Provides custom Copilot SDK tool definitions for explicit memory operations.
/// Each tool is exposed as a separate property so consumers can pick which ones to include.
/// Usage:
///   var tools = new CopilotMemoryTools(pipeline);
///   var session = await client.CreateSessionAsync(new SessionConfig
///   {
///       Tools = [tools.Store, tools.Recall, tools.Forget],
///       // or just: Tools = tools.All,
///   });
/// </summary>
public class CopilotMemoryTools
{
    private readonly MemoryPipeline _pipeline;
    private AIFunction? _store;
    private AIFunction? _recall;
    private AIFunction? _forget;

    public CopilotMemoryTools(MemoryPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    /// <summary>All three tools.</summary>
    public ICollection<AIFunction> All => [Store, Recall, Forget];

    /// <summary>Store a fact or preference in long-term memory.</summary>
    public AIFunction Store => _store ??= AIFunctionFactory.Create(
        MemoryStore, "memory_store",
        "Store a fact or preference in long-term memory. Use when the user says 'remember this' or shares important preferences/context.");

    /// <summary>Search long-term memory for relevant facts.</summary>
    public AIFunction Recall => _recall ??= AIFunctionFactory.Create(
        MemoryRecall, "memory_recall",
        "Search long-term memory for relevant facts. Use to check what you know about a topic.");

    /// <summary>Delete memories matching a query.</summary>
    public AIFunction Forget => _forget ??= AIFunctionFactory.Create(
        MemoryForget, "memory_forget",
        "Delete memories matching a query. Use when the user says 'forget this' or 'that's no longer true'.");

    private string MemoryStore(
        [Description("The fact or preference to remember")] string text,
        [Description("Who stated this: 'user' or 'assistant'")] string source = "user")
    {
        _pipeline.Store(text, source);
        return $"Stored: {text}";
    }

    private string MemoryRecall(
        [Description("What to search for in memory")] string query,
        [Description("Max results")] int limit = 5)
    {
        var results = _pipeline.Recall(query, limit);
        if (results.Count == 0) return "No relevant memories found.";
        return string.Join("\n", results.Select(r =>
            $"[{r.Source}] {r.Text} (score: {r.Score:F2})"));
    }

    private string MemoryForget(
        [Description("What to forget — matches by semantic similarity")] string query)
    {
        var deleted = _pipeline.Forget(query);
        return $"Deleted {deleted} memories matching '{query}'.";
    }
}
