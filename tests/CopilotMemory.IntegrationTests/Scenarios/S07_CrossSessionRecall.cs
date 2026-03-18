namespace CopilotMemory.IntegrationTests.Scenarios;

/// <summary>
/// S07: Cross-session recall — store in one pipeline instance, recall from a new one
/// sharing the same DuckDB file.
/// </summary>
public class S07_CrossSessionRecall
{
    [Fact]
    public async Task Memories_persist_and_recall_across_sessions()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var resultsDir = Path.Combine(
            AppContext.BaseDirectory, "test-results", timestamp, "S07-cross-session-recall");
        Directory.CreateDirectory(resultsDir);

        var dbPath = Path.Combine(resultsDir, "memory.duckdb");

        // === Session 1: Store memories ===
        using (var harness = await TestHarness.CreateWithDbAsync(dbPath, resultsDir))
        {
            await harness.Pipeline.ProcessTurnAsync(
                userMessage: "I always use NeoVim with the LazyVim distribution. My preferred colorscheme is catppuccin mocha.",
                assistantMessage: "Great setup! NeoVim with LazyVim is very productive."
            );

            var session1Memories = harness.Pipeline.GetAllMemories();
            File.WriteAllText(
                Path.Combine(resultsDir, "session1_memories.json"),
                System.Text.Json.JsonSerializer.Serialize(
                    session1Memories.Select(m => new { m.Id, m.Text, m.Source }),
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        // Pipeline disposed — DuckDB connection closed

        // === Session 2: Recall from fresh pipeline instance ===
        using (var harness = await TestHarness.CreateWithDbAsync(dbPath, resultsDir))
        {
            // Query about editor preferences — should find NeoVim memories
            var recalled = harness.Pipeline.RecallFormatted("NeoVim editor setup");

            File.WriteAllText(
                Path.Combine(resultsDir, "session2_recall.txt"), recalled);

            // Also test recall via Search to get scores
            var results = harness.Pipeline.Recall("NeoVim editor setup");

            File.WriteAllText(
                Path.Combine(resultsDir, "session2_search.json"),
                System.Text.Json.JsonSerializer.Serialize(
                    results.Select(r => new { r.Id, r.Text, r.Source, r.Score }),
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            // Write summary
            var summary = $"""
                # Scenario: S07 — Cross-Session Recall

                **Description:** Store memories in session 1, dispose pipeline, create fresh pipeline on same DB, recall in session 2.
                **Expected outcome:** Session 2 recalls NeoVim/LazyVim/catppuccin facts from session 1.
                **Timestamp:** {DateTime.UtcNow:O}
                **Model:** gpt-5-mini

                ---

                ## Session 2 Recall Output

                ```
                {recalled}
                ```

                ## Session 2 Search Results ({results.Count} matches)

                {string.Join("\n", results.Select(r => $"- [{r.Source}] {r.Text} (score: {r.Score:F3})"))}

                ## Verification Checklist

                - [ ] Session 2 recalls NeoVim/LazyVim preference from session 1
                - [ ] Recalled memories include source tags ([user] / [assistant])
                - [ ] Scores are reasonable (> 0.3)

                ## Files

                - `session1_memories.json` — memories after session 1
                - `session2_recall.txt` — formatted recall output
                - `session2_search.json` — search results with scores
                - `memory.duckdb` — shared database
                """;
            File.WriteAllText(Path.Combine(resultsDir, "summary.md"), summary);
        }
    }
}
