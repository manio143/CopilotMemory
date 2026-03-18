namespace CopilotMemory.IntegrationTests.Scenarios;

/// <summary>
/// S08: High volume, multi-topic — tests recall precision.
/// 5 turns covering different topics. Query for one topic should NOT return unrelated facts.
/// </summary>
public class S08_MultiTopicPrecision
{
    [Fact]
    public async Task Recall_returns_relevant_topic_not_unrelated()
    {
        using var harness = await TestHarness.CreateAsync("S08-multi-topic-precision");

        // Turn 1: Editor
        await harness.Pipeline.ProcessTurnAsync(
            "I use NeoVim with LazyVim. Telescope is my favorite fuzzy finder plugin.",
            "LazyVim is a great distro. Telescope is very fast with ripgrep backend.");

        // Turn 2: Database
        await harness.Pipeline.ProcessTurnAsync(
            "For databases I always go with PostgreSQL. I use pgvector for embeddings.",
            "PostgreSQL with pgvector is solid for vector search.");

        // Turn 3: Language
        await harness.Pipeline.ProcessTurnAsync(
            "I write most things in C# and TypeScript. I avoid Java whenever possible.",
            "C# and TypeScript are great modern choices.");

        // Turn 4: DevOps
        await harness.Pipeline.ProcessTurnAsync(
            "I deploy everything on Kubernetes with ArgoCD for GitOps.",
            "ArgoCD is the standard for Kubernetes GitOps deployments.");

        // Turn 5: OS
        await harness.Pipeline.ProcessTurnAsync(
            "I run Arch Linux on my workstation and NixOS on servers.",
            "Arch for dev machines and NixOS for reproducible servers is a nice combo.");

        // Recall: query about databases — should NOT return editor/OS/language facts
        var dbResults = harness.Pipeline.Recall("database preferences");
        var editorResults = harness.Pipeline.Recall("text editor setup");
        var allMemories = harness.Pipeline.GetAllMemories();

        // Write detailed results
        var summary = $"""
            # Scenario: S08 — Multi-Topic Precision

            **Description:** 5 turns on different topics. Recall should be precise per-topic.
            **Timestamp:** {DateTime.UtcNow:O}

            ---

            ## Total Memories: {allMemories.Count}

            {string.Join("\n", allMemories.Select(m => $"- [{m.Source}] {m.Text}"))}

            ## Database Query Results ({dbResults.Count} matches)

            {string.Join("\n", dbResults.Select(r => $"- [{r.Source}] {r.Text} (score: {r.Score:F3})"))}

            ## Editor Query Results ({editorResults.Count} matches)

            {string.Join("\n", editorResults.Select(r => $"- [{r.Source}] {r.Text} (score: {r.Score:F3})"))}

            ## Verification

            - [ ] Database query returns PostgreSQL/pgvector facts
            - [ ] Database query does NOT return NeoVim/Arch/C# facts
            - [ ] Editor query returns NeoVim/LazyVim/Telescope facts
            - [ ] Editor query does NOT return PostgreSQL/Kubernetes facts
            - [ ] Total memory count is reasonable (not inflated by duplicates)
            """;
        File.WriteAllText(Path.Combine(harness.ResultsDir, "summary.md"), summary);
    }
}
