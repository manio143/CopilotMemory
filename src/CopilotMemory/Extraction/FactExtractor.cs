using System.Text.Json;

namespace CopilotMemory.Extraction;

/// <summary>
/// Extracts facts from conversations using LLM prompts.
/// Uses a single combined prompt to extract both user and assistant facts.
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
    /// Extracts facts from both user and assistant messages in a single LLM call.
    /// Each fact is tagged with its source ("user" or "assistant").
    /// </summary>
    /// <param name="conversation">Full conversation text (user and assistant messages).</param>
    /// <returns>List of extracted facts with source attribution.</returns>
    public async Task<List<Fact>> ExtractFactsAsync(string conversation)
    {
        var systemPrompt = Prompts.CombinedFactExtraction(DateTime.UtcNow);
        var response = await _llm.CompleteAsync(systemPrompt, $"Extract facts from this conversation:\n\n{conversation}");
        return ParseCombinedFacts(response);
    }

    internal static List<Fact> ParseCombinedFacts(string response)
    {
        var json = StripCodeFences(response);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var facts = doc.RootElement.GetProperty("facts");
            return facts.EnumerateArray()
                .Select(f => new Fact
                {
                    Text = f.GetProperty("text").GetString() ?? "",
                    Source = f.TryGetProperty("source", out var src)
                        ? src.GetString() ?? "user"
                        : "user",
                })
                .Where(f => !string.IsNullOrWhiteSpace(f.Text))
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException)
        {
            return [];
        }
    }

    internal static List<Fact> ParseFacts(string response, string source)
    {
        var json = StripCodeFences(response);

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

    private static string StripCodeFences(string response)
    {
        var json = response.Trim();
        if (json.StartsWith("```"))
        {
            var firstNewline = json.IndexOf('\n');
            var lastFence = json.LastIndexOf("```");
            if (firstNewline >= 0 && lastFence > firstNewline)
                json = json[(firstNewline + 1)..lastFence].Trim();
        }
        return json;
    }
}
