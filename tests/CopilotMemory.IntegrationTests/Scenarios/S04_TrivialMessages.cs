namespace CopilotMemory.IntegrationTests.Scenarios;

public class S04_TrivialMessages
{
    [Fact]
    public async Task Trivial_conversation_produces_no_memories()
    {
        using var harness = await TestHarness.CreateAsync("S04-trivial-messages");

        await harness.Pipeline.ProcessTurnAsync(
            userMessage: "Hi",
            assistantMessage: "Hello! How can I help you today?"
        );

        await harness.Pipeline.ProcessTurnAsync(
            userMessage: "Thanks",
            assistantMessage: "You're welcome!"
        );

        await harness.Pipeline.ProcessTurnAsync(
            userMessage: "Ok bye",
            assistantMessage: "Goodbye!"
        );

        harness.DumpResults(
            description: "Three trivial conversation turns (greetings, thanks, bye). No meaningful facts should be extracted.",
            expectedOutcome: "Zero memories in the store. All extraction steps return empty facts arrays."
        );
    }
}
