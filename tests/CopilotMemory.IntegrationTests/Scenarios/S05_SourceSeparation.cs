namespace CopilotMemory.IntegrationTests.Scenarios;

public class S05_SourceSeparation
{
    [Fact]
    public async Task User_and_assistant_facts_tagged_correctly()
    {
        using var harness = await TestHarness.CreateAsync("S05-source-separation");

        await harness.Pipeline.ProcessTurnAsync(
            userMessage: "I prefer PostgreSQL for production databases. What do you think about Redis?",
            assistantMessage: "Redis is great for caching and session storage. I'd recommend using Redis alongside PostgreSQL — Redis for hot data caching and PostgreSQL as the primary store."
        );

        // Check source attribution
        var memories = harness.Pipeline.GetAllMemories();
        var userMemories = memories.Where(m => m.Source == "user").ToList();
        var assistantMemories = memories.Where(m => m.Source == "assistant").ToList();

        File.WriteAllText(
            Path.Combine(harness.ResultsDir, "source_breakdown.json"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                user = userMemories.Select(m => m.Text),
                assistant = assistantMemories.Select(m => m.Text),
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        harness.DumpResults(
            description: "Single turn where user states a preference and assistant makes a recommendation. Both should be captured with correct source tags.",
            expectedOutcome: "User facts: PostgreSQL preference (source=user). Assistant facts: Redis recommendation (source=assistant). No cross-contamination."
        );
    }
}
