using Microsoft.Extensions.AI;

namespace CopilotMemory.IntegrationTests.Scenarios;

/// <summary>
/// S09: Forget via tool — tests memory_forget tool deletes matching memories
/// and leaves unrelated ones intact. Exercises CopilotMemoryTools, not pipeline directly.
/// </summary>
public class S09_ForgetTool
{
    [Fact]
    public async Task Forget_tool_deletes_matching_and_preserves_others()
    {
        using var harness = await TestHarness.CreateAsync("S09-forget-tool");
        var tools = new CopilotMemoryTools(harness.Pipeline);

        // Store some facts via the store tool
        await tools.Store.InvokeAsync(new AIFunctionArguments
        {
            ["text"] = "Prefers PostgreSQL for all production databases",
            ["source"] = "user"
        });
        await tools.Store.InvokeAsync(new AIFunctionArguments
        {
            ["text"] = "Uses NeoVim with LazyVim distribution",
            ["source"] = "user"
        });
        await tools.Store.InvokeAsync(new AIFunctionArguments
        {
            ["text"] = "Deploys on Kubernetes with ArgoCD",
            ["source"] = "user"
        });

        var beforeCount = harness.Pipeline.GetAllMemories().Count;

        // Recall via tool to verify they're there
        var recallResult = await tools.Recall.InvokeAsync(new AIFunctionArguments
        {
            ["query"] = "PostgreSQL database",
            ["limit"] = 5
        });

        // Forget PostgreSQL via tool
        var forgetResult = await tools.Forget.InvokeAsync(new AIFunctionArguments
        {
            ["query"] = "PostgreSQL database"
        });

        var afterMemories = harness.Pipeline.GetAllMemories();

        // Recall again — should not find PostgreSQL
        var recallAfter = await tools.Recall.InvokeAsync(new AIFunctionArguments
        {
            ["query"] = "PostgreSQL database",
            ["limit"] = 5
        });

        var summary = $"""
            # Scenario: S09 — Forget Tool

            **Description:** Store 3 facts via memory_store tool, forget PostgreSQL via memory_forget tool.
            **Timestamp:** {DateTime.UtcNow:O}

            ---

            ## Before Forget: {beforeCount} memories
            ## After Forget: {afterMemories.Count} memories

            ## Remaining Memories

            {string.Join("\n", afterMemories.Select(m => $"- [{m.Source}] {m.Text}"))}

            ## Recall Before Forget

            ```
            {recallResult}
            ```

            ## Forget Result

            ```
            {forgetResult}
            ```

            ## Recall After Forget

            ```
            {recallAfter}
            ```

            ## Verification

            - [ ] PostgreSQL memory was deleted
            - [ ] NeoVim and Kubernetes memories survived
            - [ ] Forget result says "Deleted 1 memories"
            - [ ] Post-forget recall returns no PostgreSQL matches
            - [ ] All operations went through tool interface (AIFunction.InvokeAsync)
            """;
        File.WriteAllText(Path.Combine(harness.ResultsDir, "summary.md"), summary);
    }
}
