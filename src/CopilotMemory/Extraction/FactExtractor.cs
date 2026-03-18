using System.Text.Json;

namespace CopilotMemory.Extraction;

internal class FactExtractor
{
    private readonly ILlmClient _llm;

    public FactExtractor(ILlmClient llm)
    {
        _llm = llm;
    }

    public async Task<List<Fact>> ExtractUserFactsAsync(string conversation)
    {
        var systemPrompt = Prompts.UserFactExtraction(DateTime.UtcNow);
        var response = await _llm.CompleteAsync(systemPrompt, conversation);
        return ParseFacts(response, "user");
    }

    public async Task<List<Fact>> ExtractAssistantFactsAsync(string conversation)
    {
        var systemPrompt = Prompts.AssistantFactExtraction(DateTime.UtcNow);
        var response = await _llm.CompleteAsync(systemPrompt, conversation);
        return ParseFacts(response, "assistant");
    }

    internal static List<Fact> ParseFacts(string response, string source)
    {
        // Strip markdown code fences if present
        var json = response.Trim();
        if (json.StartsWith("```"))
        {
            var firstNewline = json.IndexOf('\n');
            var lastFence = json.LastIndexOf("```");
            if (firstNewline >= 0 && lastFence > firstNewline)
                json = json[(firstNewline + 1)..lastFence].Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var facts = doc.RootElement.GetProperty("facts");
            return facts.EnumerateArray()
                .Select(f => new Fact { Text = f.GetString() ?? "", Source = source })
                .Where(f => !string.IsNullOrWhiteSpace(f.Text))
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException)
        {
            return [];
        }
    }
}
