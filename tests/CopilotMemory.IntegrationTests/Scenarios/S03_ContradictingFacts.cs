namespace CopilotMemory.IntegrationTests.Scenarios;

public class S03_ContradictingFacts
{
    [Fact]
    public async Task Contradicting_preference_triggers_update()
    {
        using var harness = await TestHarness.CreateAsync("S03-contradicting-facts");

        // Turn 1: establish preference
        await harness.Pipeline.ProcessTurnAsync(
            userMessage: "My preferred database is PostgreSQL for everything.",
            assistantMessage: "Noted — PostgreSQL for all your database needs."
        );

        // Snapshot after turn 1
        var afterTurn1 = harness.Pipeline.GetAllMemories();
        File.WriteAllText(
            Path.Combine(harness.ResultsDir, "after_turn1.json"),
            System.Text.Json.JsonSerializer.Serialize(
                afterTurn1.Select(m => new { m.Id, m.Text, m.Source }),
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        harness.ClearEvents();

        // Turn 2: contradict it
        await harness.Pipeline.ProcessTurnAsync(
            userMessage: "Actually, I've switched to SQLite for this project. It's simpler for embedded use.",
            assistantMessage: "Makes sense — SQLite is great for embedded scenarios."
        );

        harness.DumpResults(
            description: "User states PostgreSQL preference, then contradicts it with SQLite. Tests the dedup gate (>0.95 similarity) and LLM UPDATE/DELETE decision.",
            expectedOutcome: "PostgreSQL memory either updated to mention SQLite or deleted. SQLite fact added. No duplicate PostgreSQL+SQLite memories."
        );
    }
}
