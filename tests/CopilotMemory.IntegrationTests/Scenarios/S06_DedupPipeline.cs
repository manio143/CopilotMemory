namespace CopilotMemory.IntegrationTests.Scenarios;

public class S06_DedupPipeline
{
    [Fact]
    public async Task Duplicate_fact_is_not_stored_twice_enriched_fact_updates()
    {
        using var harness = await TestHarness.CreateAsync("S06-dedup-pipeline");

        // Turn 1: establish fact
        await harness.Pipeline.ProcessTurnAsync(
            userMessage: "I prefer using PostgreSQL for all my production databases.",
            assistantMessage: "PostgreSQL is a solid choice for production workloads."
        );

        var afterTurn1 = harness.Pipeline.GetAllMemories();
        File.WriteAllText(
            Path.Combine(harness.ResultsDir, "after_turn1.json"),
            System.Text.Json.JsonSerializer.Serialize(
                afterTurn1.Select(m => new { m.Id, m.Text, m.Source }),
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        harness.ClearEvents();

        // Turn 2: same fact rephrased (should NOT create duplicate)
        await harness.Pipeline.ProcessTurnAsync(
            userMessage: "For production I always go with PostgreSQL as my database.",
            assistantMessage: "Makes sense, it has great reliability."
        );

        var afterTurn2 = harness.Pipeline.GetAllMemories();
        File.WriteAllText(
            Path.Combine(harness.ResultsDir, "after_turn2.json"),
            System.Text.Json.JsonSerializer.Serialize(
                afterTurn2.Select(m => new { m.Id, m.Text, m.Source }),
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        harness.ClearEvents();

        // Turn 3: enriched fact (should UPDATE existing)
        await harness.Pipeline.ProcessTurnAsync(
            userMessage: "I use PostgreSQL for production because of its reliability and JSONB support.",
            assistantMessage: "JSONB is indeed one of PostgreSQL's best features."
        );

        harness.DumpResults(
            description: "Three turns: (1) establish PostgreSQL preference, (2) rephrase same preference (should not duplicate), (3) enrich with reasons (should UPDATE). Tests the full dedup pipeline with real embeddings and similarity checks.",
            expectedOutcome: "After turn 2: still 1 PostgreSQL memory (NONE decision). After turn 3: PostgreSQL memory updated to include reliability and JSONB (UPDATE decision). Total user memories about PostgreSQL: 1, not 3."
        );
    }
}
