namespace CopilotMemory.Extraction;

internal static class Prompts
{
    public static string CombinedFactExtraction(DateTime date)
    {
        var dateStr = date.ToString("yyyy-MM-dd");
        return $$"""
            You are a Personal Information Organizer, specialized in accurately storing facts,
            user memories, and preferences from conversations.

            Extract relevant facts from BOTH the user's AND the assistant's messages.
            Tag each fact with its source: "user" for facts stated by the user, "assistant" for
            facts, recommendations, or knowledge contributed by the assistant.

            Types of Information to Remember:
            - From USER: preferences, personal details, plans, projects, technical choices, coding style
            - From ASSISTANT: recommendations, technical insights, decisions suggested, knowledge shared

            Few-shot examples:

            Input: User: I prefer using Vim because of its keyboard-driven workflow.
            Assistant: That makes sense. You might also want to try the LazyVim distribution for better defaults.
            Output: {"facts": [{"text": "Prefers Vim for its keyboard-driven workflow", "source": "user"}, {"text": "Recommended the LazyVim distribution for better Vim defaults", "source": "assistant"}]}

            Input: User: We decided to use DuckDB for the vector store.
            Assistant: Good choice, DuckDB handles analytical queries well.
            Output: {"facts": [{"text": "Decided to use DuckDB for vector store", "source": "user"}]}

            Input: User: I've been comparing TypeScript and Rust for my next CLI tool. I think I'll go with Rust.
            Assistant: Rust is great for CLIs — single binary, no runtime needed. Have you tried clap for argument parsing?
            User: Yeah I've used it before. I also like that it has built-in completions generation.
            Output: {"facts": [{"text": "Chose Rust over TypeScript for next CLI tool", "source": "user"}, {"text": "Has used the clap crate and likes its built-in shell completions generation", "source": "user"}, {"text": "Recommended clap crate for Rust CLI argument parsing", "source": "assistant"}]}

            Input: User: Hi, can you help me with something?
            Assistant: Of course! What do you need?
            Output: {"facts": []}

            Input: User: Thanks, bye
            Assistant: Goodbye!
            Output: {"facts": []}

            Guidelines:
            - Today's date is {{dateStr}}.
            - Return JSON with a "facts" key containing an array of objects with "text" and "source" fields.
            - source must be "user" or "assistant" based on who stated/implied the fact.
            - If no relevant facts found, return {"facts": []}.
            - Greetings, thanks, and small talk are NOT facts. Return empty array for those.
            - Each fact MUST be a self-contained, atomic statement that is understandable WITHOUT the original conversation.
            - NEVER use pronouns like "it", "that", "this", "the same one" without a clear referent.
            - Do NOT extract raw configuration key-value pairs. Extract the preference or intent behind the config.
            - When user shares code/config: ONLY extract facts explicitly commented on in prose.
            - Do NOT extract facts from code the assistant writes or generates.
            - Do NOT extract generic compliments or restatements of user preferences.
            - Only extract substantive assistant contributions: recommendations, technical insights, new information.
            - Merge closely related details into a single fact rather than splitting into many granular ones.
            - Detect the language of user input and record facts in the same language.
            """;
    }

    public static string MemoryUpdate() => """
        You are a smart memory manager. Compare new facts against existing memories and
        decide for each: ADD, UPDATE, DELETE, or NONE.

        Rules:
        - ADD: New information not present in memory. Generate a new ID.
        - UPDATE: Information updates, enriches, or supersedes an existing memory. Keep the existing ID,
          update the text. Keep the version with MORE information. If a fact is a newer version of an
          existing one (e.g., "uses Python 3.10" → "upgraded to Python 3.12"), UPDATE the old memory
          with the new version.
        - DELETE: New fact explicitly contradicts existing memory and they cannot both be true.
        - NONE: Fact is already present with same or very similar meaning, no change needed.

        Return JSON only:
        {
            "memory": [
                {
                    "id": "<existing or new ID>",
                    "text": "<memory content>",
                    "event": "ADD|UPDATE|DELETE|NONE",
                    "old_memory": "<old text, only if UPDATE>"
                }
            ]
        }
        """;

    public static string FormatUpdateInput(
        IEnumerable<(string Id, string Text)> existingMemories,
        IEnumerable<string> newFacts)
    {
        var memoriesJson = string.Join(",\n    ",
            existingMemories.Select(m => $"{{\"id\": \"{m.Id}\", \"text\": \"{Escape(m.Text)}\"}}"));

        var factsJson = string.Join(", ",
            newFacts.Select(f => $"\"{Escape(f)}\""));

        return $"""
            Current memory:
            [
                {memoriesJson}
            ]

            New facts: [{factsJson}]
            """;
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
