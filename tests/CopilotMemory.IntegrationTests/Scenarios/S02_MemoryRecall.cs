namespace CopilotMemory.IntegrationTests.Scenarios;

public class S02_MemoryRecall
{
    [Fact]
    public async Task Recall_returns_relevant_memories_with_source_tags()
    {
        using var harness = await TestHarness.CreateAsync("S02-memory-recall");

        // Build up some memories
        await harness.Pipeline.ProcessTurnAsync(
            userMessage: "I prefer PostgreSQL for production databases.",
            assistantMessage: "PostgreSQL is a solid choice for production."
        );

        await harness.Pipeline.ProcessTurnAsync(
            userMessage: "What's a good caching layer?",
            assistantMessage: "Redis is excellent for caching — fast, supports TTL, and has great .NET client libraries like StackExchange.Redis."
        );

        harness.ClearEvents();

        // Now recall
        var formatted = harness.Pipeline.RecallFormatted("database preferences");
        File.WriteAllText(
            Path.Combine(harness.ResultsDir, "recall_output.txt"),
            formatted);

        harness.DumpResults(
            description: "Store facts from two turns, then recall with a query. Verify relevant memories returned with [user]/[assistant] source tags.",
            expectedOutcome: "Recall output contains PostgreSQL preference tagged [user]. May contain Redis recommendation tagged [assistant]. Format uses <relevant-memories> wrapper."
        );
    }
}
