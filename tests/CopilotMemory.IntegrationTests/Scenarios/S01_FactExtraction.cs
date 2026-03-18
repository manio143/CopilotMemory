namespace CopilotMemory.IntegrationTests.Scenarios;

public class S01_FactExtraction
{
    [Fact]
    public async Task User_states_multiple_preferences()
    {
        using var harness = await TestHarness.CreateAsync("S01-fact-extraction");

        await harness.Pipeline.ProcessTurnAsync(
            userMessage: "My name is Marian. I'm a .NET developer and I prefer using Vim because of its keyboard-driven workflow. I use 4-space indentation, never tabs.",
            assistantMessage: "Nice to meet you, Marian! I'll keep your Vim and indentation preferences in mind."
        );

        harness.DumpResults(
            description: "User states name, profession, editor preference, and indentation style in one message.",
            expectedOutcome: "Multiple distinct facts extracted: name, .NET developer, Vim preference, 4-space indentation. All tagged as source=user."
        );
    }
}
