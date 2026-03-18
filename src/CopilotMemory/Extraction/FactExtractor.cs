using System.Text.Json;

namespace CopilotMemory.Extraction;

/// <summary>
/// Extracts facts from conversations using LLM prompts.
/// Separate methods for user facts and assistant facts.
/// </summary>
public class FactExtractor
{
    private readonly ILlmClient _llm;

    /// <summary>
    /// Creates a new fact extractor with the specified LLM client.
    /// </summary>
    /// <param name="llm">LLM client for fact extraction.</param>
    public FactExtractor(ILlmClient llm)
    {
        _llm = llm;
    }

    /// <summary>
    /// Extracts facts from the user's messages in a conversation.
    /// </summary>
    /// <param name="conversation">Full conversation text (user and assistant messages).</param>
    /// <returns>List of extracted facts attributed to the user.</returns>
    public async Task<List<Fact>> ExtractUserFactsAsync(string conversation)
    {
        var systemPrompt = Prompts.UserFactExtraction(DateTime.UtcNow);
        var response = await _llm.CompleteAsync(systemPrompt, conversation);
        return ParseFacts(response, "user");
    }

    /// <summary>
    /// Extracts facts from the assistant's messages in a conversation.
    /// </summary>
    /// <param name="conversation">Full conversation text (user and assistant messages).</param>
    /// <returns>List of extracted facts attributed to the assistant.</returns>
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
