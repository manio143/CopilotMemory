using System.Text.Json;
using CopilotMemory.Embeddings;
using CopilotMemory.Extraction;
using CopilotMemory.Store;
using GitHub.Copilot.SDK;

namespace CopilotMemory.IntegrationTests;

public class TestHarness : IDisposable
{
    public MemoryPipeline Pipeline { get; }
    public string ResultsDir { get; }
    private readonly List<MemoryPipelineEvent> _events = [];
    private readonly SdkLlmClient? _sdkClient;

    private TestHarness(MemoryPipeline pipeline, string resultsDir, SdkLlmClient? sdkClient = null)
    {
        Pipeline = pipeline;
        ResultsDir = resultsDir;
        _sdkClient = sdkClient;
        pipeline.On(e => _events.Add(e));
    }

    public static async Task<TestHarness> CreateAsync(string scenarioName)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var resultsDir = Path.Combine(
            AppContext.BaseDirectory, "test-results", timestamp, scenarioName);
        Directory.CreateDirectory(resultsDir);

        var dbPath = Path.Combine(resultsDir, "memory.duckdb");
        return await CreateWithDbAsync(dbPath, resultsDir);
    }

    public static async Task<TestHarness> CreateWithDbAsync(string dbPath, string resultsDir)
    {
        Directory.CreateDirectory(resultsDir);
        var store = new DuckDbMemoryStore(dbPath);
        var embedder = new LocalEmbeddingService();
        var sdkClient = new SdkLlmClient("gpt-5-mini");
        await sdkClient.EnsureStartedAsync();
        var extractor = new FactExtractor(sdkClient);
        var updater = new MemoryUpdater(sdkClient);
        var pipeline = new MemoryPipeline(store, embedder, extractor, updater);

        return new TestHarness(pipeline, resultsDir, sdkClient);
    }

    public void ClearEvents() => _events.Clear();

    public void DumpResults(string description, string expectedOutcome)
    {
        // Dump events
        var eventsJson = JsonSerializer.Serialize(_events.Select(e => new { e.Step, e.Detail, e.DurationMs }),
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(ResultsDir, "pipeline_events.json"), eventsJson);

        // Dump memory state
        var memories = Pipeline.GetAllMemories();
        var memoryJson = JsonSerializer.Serialize(
            memories.Select(m => new { m.Id, m.Text, m.Source, m.CreatedAt, m.UpdatedAt }),
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(ResultsDir, "memory_state.json"), memoryJson);

        var stats = Pipeline.GetStats();

        var summary = $"""
            # Scenario: {Path.GetFileName(ResultsDir)}

            **Description:** {description}
            **Expected outcome:** {expectedOutcome}
            **Timestamp:** {DateTime.UtcNow:O}
            **Model:** gpt-5-mini

            ---

            ## Memory State ({stats.Count} memories)

            {string.Join("\n", memories.Select(m => $"- [{m.Source}] {m.Text}"))}

            ## Pipeline Events ({_events.Count} events)

            {string.Join("\n", _events.Select(e => $"- **{e.Step}** ({e.DurationMs}ms): {Truncate(e.Detail, 200)}"))}

            ## Verification Checklist

            - [ ] {expectedOutcome}

            ## Files

            - `pipeline_events.json` — step-by-step events
            - `memory_state.json` — final memory store
            - `memory.duckdb` — raw database
            """;
        File.WriteAllText(Path.Combine(ResultsDir, "summary.md"), summary);
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "...";

    public void Dispose()
    {
        Pipeline.Dispose();
        if (_sdkClient != null)
        {
            _sdkClient.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
