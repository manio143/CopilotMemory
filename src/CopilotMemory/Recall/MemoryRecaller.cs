namespace CopilotMemory.Recall;

using CopilotMemory.Store;

/// <summary>
/// Formats memory recall results for inclusion in LLM context.
/// Converts search results into human-readable context blocks.
/// </summary>
public class MemoryRecaller
{
    /// <summary>
    /// Formats a list of search results into a structured context block
    /// suitable for injection into LLM prompts.
    /// </summary>
    /// <param name="memories">List of search results to format.</param>
    /// <returns>Formatted context string with XML tags, or empty string if no memories.</returns>
    public static string FormatMemories(List<SearchResult> memories)
    {
        if (memories.Count == 0) return "";

        var lines = memories.Select(m => $"- [{m.Source}] {m.Text}");

        return string.Join("\n", new[]
        {
            "<relevant-memories>",
            "The following memories may be relevant. Source indicates origin (user/assistant).",
        }.Concat(lines).Append("</relevant-memories>"));
    }
}
