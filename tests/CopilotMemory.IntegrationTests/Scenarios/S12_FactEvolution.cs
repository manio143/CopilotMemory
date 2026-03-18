namespace CopilotMemory.IntegrationTests.Scenarios;

/// <summary>
/// S12: Same-fact evolution — a fact gets updated (not contradicted by a different thing).
/// "Uses Python 3.10" → "Upgraded to Python 3.12" should result in ONE memory with 3.12.
/// </summary>
public class S12_FactEvolution
{
    [Fact]
    public async Task Updated_fact_replaces_original()
    {
        using var harness = await TestHarness.CreateAsync("S12-fact-evolution");

        // Turn 1: Establish baseline
        await harness.Pipeline.ProcessTurnAsync(
            "I'm using Python 3.10 for my data science projects.",
            "Python 3.10 is solid. Structural pattern matching is nice for data pipelines.");

        var afterTurn1 = harness.Pipeline.GetAllMemories().ToList();
        harness.ClearEvents();

        // Turn 2: Upgrade same fact
        await harness.Pipeline.ProcessTurnAsync(
            "I just upgraded to Python 3.12 for the performance improvements. The f-string changes are also nice.",
            "Great upgrade! Python 3.12 has significant perf gains, especially for comprehensions.");

        var afterTurn2 = harness.Pipeline.GetAllMemories().ToList();

        // Recall: should only return 3.12, not 3.10
        var recallResults = harness.Pipeline.Recall("Python version");

        var summary = $"""
            # Scenario: S12 — Fact Evolution

            **Description:** User states Python 3.10, then upgrades to 3.12. Should UPDATE, not ADD.
            **Expected:** Single memory mentioning Python 3.12. No stale 3.10 reference.
            **Timestamp:** {DateTime.UtcNow:O}

            ---

            ## After Turn 1 ({afterTurn1.Count} memories)

            {string.Join("\n", afterTurn1.Select(m => $"- [{m.Source}] {m.Text}"))}

            ## After Turn 2 ({afterTurn2.Count} memories)

            {string.Join("\n", afterTurn2.Select(m => $"- [{m.Source}] {m.Text}"))}

            ## Recall "Python version" ({recallResults.Count} results)

            {string.Join("\n", recallResults.Select(r => $"- [{r.Source}] {r.Text} (score: {r.Score:F3})"))}

            ## Verification

            - [ ] Only ONE memory about Python version exists after turn 2
            - [ ] That memory mentions Python 3.12 (not 3.10)
            - [ ] Recall for "Python version" returns 3.12 as top result
            - [ ] No stale 3.10 memory remains
            """;
        File.WriteAllText(Path.Combine(harness.ResultsDir, "summary.md"), summary);
    }
}
