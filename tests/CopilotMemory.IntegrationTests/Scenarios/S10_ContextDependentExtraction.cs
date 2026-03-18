namespace CopilotMemory.IntegrationTests.Scenarios;

/// <summary>
/// S10: Context-dependent extraction — facts that only make sense with prior context.
/// Tests whether extraction handles anaphora ("that", "it", "the same one") correctly.
/// </summary>
public class S10_ContextDependentExtraction
{
    [Fact]
    public async Task Extraction_resolves_anaphora_from_conversation_context()
    {
        using var harness = await TestHarness.CreateAsync("S10-context-dependent");

        // The conversation has context that resolves "that" and "it"
        await harness.Pipeline.ProcessTurnAsync(
            userMessage: "I've been comparing TypeScript and Rust for my next CLI tool. I think I'll go with Rust because of the binary distribution story.",
            assistantMessage: "Rust is great for CLIs — single binary, no runtime needed. Have you tried clap for argument parsing?"
        );

        await harness.Pipeline.ProcessTurnAsync(
            userMessage: "Yeah I've used it before. I also like that it has built-in completions generation.",
            assistantMessage: "Clap's derive API with completions is really nice. You can generate bash/zsh/fish completions automatically."
        );

        var memories = harness.Pipeline.GetAllMemories();

        var summary = $"""
            # Scenario: S10 — Context-Dependent Extraction

            **Description:** Conversation with anaphora ("it", "that") — extraction must resolve references from context.
            **Expected outcome:** Facts should be self-contained (e.g., "Uses clap for argument parsing" not "Has used it before").
            **Timestamp:** {DateTime.UtcNow:O}

            ---

            ## Memories ({memories.Count})

            {string.Join("\n", memories.Select(m => $"- [{m.Source}] {m.Text}"))}

            ## Verification

            - [ ] No facts contain unresolved pronouns ("it", "that", "the same one")
            - [ ] Facts are self-contained and understandable without conversation context
            - [ ] Rust preference for CLI tools is captured
            - [ ] Clap usage/preference is captured
            - [ ] No garbage facts like "Has used it before"
            """;
        File.WriteAllText(Path.Combine(harness.ResultsDir, "summary.md"), summary);
    }
}
