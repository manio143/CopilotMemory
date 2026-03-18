using System.Text.Json;

namespace CopilotMemory.Extraction;

internal class MemoryUpdater
{
    private readonly ILlmClient _llm;

    public MemoryUpdater(ILlmClient llm)
    {
        _llm = llm;
    }

    public async Task<List<UpdateDecision>> DecideAsync(
        IEnumerable<(string Id, string Text)> existingMemories,
        IEnumerable<string> newFacts)
    {
        var systemPrompt = Prompts.MemoryUpdate();
        var input = Prompts.FormatUpdateInput(existingMemories, newFacts);
        var response = await _llm.CompleteAsync(systemPrompt, input);
        return ParseDecisions(response);
    }

    internal static List<UpdateDecision> ParseDecisions(string response)
    {
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
            var memory = doc.RootElement.GetProperty("memory");
            return memory.EnumerateArray().Select(m =>
            {
                var eventStr = m.GetProperty("event").GetString()?.ToUpperInvariant() ?? "NONE";
                return new UpdateDecision
                {
                    Id = m.GetProperty("id").GetString() ?? "",
                    Text = m.GetProperty("text").GetString() ?? "",
                    Event = Enum.TryParse<UpdateEvent>(eventStr, out var e) ? e : UpdateEvent.NONE,
                    OldMemory = m.TryGetProperty("old_memory", out var old) ? old.GetString() : null,
                };
            }).ToList();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException)
        {
            return [];
        }
    }
}
