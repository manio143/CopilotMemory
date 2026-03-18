using System.Text.Json;

namespace CopilotMemory.Extraction;

/// <summary>
/// Decides how to handle new facts against existing memories (ADD/UPDATE/DELETE/NONE).
/// Uses LLM to make intelligent deduplication decisions.
/// </summary>
public class MemoryUpdater
{
    private readonly ILlmClient _llm;

    /// <summary>
    /// Creates a new memory updater with the specified LLM client.
    /// </summary>
    /// <param name="llm">LLM client for update decisions.</param>
    public MemoryUpdater(ILlmClient llm)
    {
        _llm = llm;
    }

    /// <summary>
    /// Decides what to do with new facts given existing memories.
    /// Returns a list of update decisions (ADD/UPDATE/DELETE/NONE) for each fact.
    /// </summary>
    /// <param name="existingMemories">Existing memory entries (ID and text).</param>
    /// <param name="newFacts">New facts to compare against existing memories.</param>
    /// <returns>List of update decisions for each fact/memory pair.</returns>
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
