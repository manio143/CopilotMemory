namespace CopilotMemory.IntegrationTests.Scenarios;

/// <summary>
/// S11: Code snippets and config — should the pipeline extract facts from code,
/// or treat it as noise? Tests extraction robustness with mixed prose + code input.
/// </summary>
public class S11_CodeSnippets
{
    [Fact]
    public async Task Extraction_handles_code_snippets_sensibly()
    {
        using var harness = await TestHarness.CreateAsync("S11-code-snippets");

        await harness.Pipeline.ProcessTurnAsync(
            userMessage: """
                Here's my tsconfig.json I use for all projects:
                ```json
                {
                  "compilerOptions": {
                    "strict": true,
                    "target": "ES2022",
                    "module": "NodeNext",
                    "noUncheckedIndexedAccess": true
                  }
                }
                ```
                I always enable strict mode and noUncheckedIndexedAccess.
                """,
            assistantMessage: "Solid TypeScript config. Strict mode with noUncheckedIndexedAccess catches a lot of bugs at compile time. ES2022 target gives you top-level await too."
        );

        var memories = harness.Pipeline.GetAllMemories();

        var summary = $"""
            # Scenario: S11 — Code Snippets

            **Description:** User shares a tsconfig.json with commentary. Tests whether extraction focuses on preferences/facts rather than raw config values.
            **Timestamp:** {DateTime.UtcNow:O}

            ---

            ## Memories ({memories.Count})

            {string.Join("\n", memories.Select(m => $"- [{m.Source}] {m.Text}"))}

            ## Verification

            - [ ] Extracted preference for strict mode (from prose, not just config key)
            - [ ] Extracted noUncheckedIndexedAccess preference
            - [ ] Did NOT store raw JSON config as a "fact"
            - [ ] Did NOT store individual compiler options as separate facts (target, module, etc.)
            - [ ] Facts are human-readable preferences, not config key-value pairs
            """;
        File.WriteAllText(Path.Combine(harness.ResultsDir, "summary.md"), summary);
    }
}
