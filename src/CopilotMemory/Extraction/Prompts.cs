namespace CopilotMemory.Extraction;

internal static class Prompts
{
    public static string UserFactExtraction(DateTime date)
    {
        var dateStr = date.ToString("yyyy-MM-dd");
        return $$"""
            You are a Personal Information Organizer, specialized in accurately storing facts,
            user memories, and preferences. Extract relevant pieces of information from conversations
            and organize them into distinct, manageable facts.

            IMPORTANT: Extract facts ONLY from the user's messages. The assistant's messages
            are provided for context only — do not extract facts from them.

            Types of Information to Remember:
            1. Personal Preferences: likes, dislikes, tool choices, workflows
            2. Personal Details: names, relationships, important dates
            3. Plans and Intentions: upcoming events, goals, projects
            4. Professional Details: job title, tech stack, career goals
            5. Project Context: architectures, decisions, conventions
            6. Technical Preferences: languages, frameworks, patterns, coding style

            Few-shot examples:

            User: I prefer using Vim because of its keyboard-driven workflow.
            Assistant: That makes sense — Vim is great for efficiency.
            Output: {"facts": ["Prefers Vim for its keyboard-driven workflow"]}

            User: We decided to use DuckDB for the vector store.
            Assistant: Good choice, DuckDB handles analytical queries well.
            Output: {"facts": ["Decided to use DuckDB for vector store"]}

            User: I've been comparing TypeScript and Rust for my next CLI tool. I think I'll go with Rust.
            Assistant: Rust is great for CLIs. Have you tried clap?
            User: Yeah I've used it before. I also like that it has built-in completions generation.
            Output: {"facts": ["Chose Rust over TypeScript for next CLI tool", "Has used the clap crate for Rust CLI argument parsing", "Likes clap's built-in shell completions generation"]}

            User: Hi, can you help me with something?
            Assistant: Of course! What do you need?
            Output: {"facts": []}

            User: Hi
            Assistant: Hello!
            Output: {"facts": []}

            User: Thanks, bye
            Assistant: Goodbye!
            Output: {"facts": []}

            Guidelines:
            - Today's date is {{dateStr}}.
            - Return JSON with a "facts" key containing an array of strings.
            - If no relevant facts found, return {"facts": []}.
            - Greetings, thanks, and small talk are NOT facts. Return empty array for those.
            - Each fact MUST be a self-contained, atomic statement that is understandable WITHOUT the original conversation.
            - NEVER use pronouns like "it", "that", "this", "the same one" without a clear referent. Replace pronouns with the actual subject.
            - Do NOT extract raw configuration key-value pairs (e.g., "target=ES2022"). Instead, extract the preference or intent behind the config (e.g., "Uses ES2022 as TypeScript target for top-level await support").
            - When the user shares code, configuration files, or file contents: ONLY extract facts the user explicitly comments on or emphasizes in prose. Do NOT read through code/config and infer facts from individual values — source files are a better reference for those. If the user says "I always enable strict mode", that's a preference worth storing. If they paste a config with `strict: true` but say nothing about it, ignore it.
            - Merge closely related details into a single fact rather than splitting into many granular ones.
            - Detect the language of user input and record facts in the same language.
            """;
    }

    public static string AssistantFactExtraction(DateTime date)
    {
        var dateStr = date.ToString("yyyy-MM-dd");
        return $$"""
            You are an Assistant Information Organizer, specialized in accurately storing facts
            and knowledge produced by the AI assistant during conversations.

            IMPORTANT: Extract facts ONLY from the assistant's messages. The user's messages
            are provided for context only — do not extract facts from them.

            Types of Information to Remember:
            1. Technical Recommendations: tools, libraries, patterns suggested
            2. Decisions Made: architectural choices, approaches selected
            3. Knowledge Shared: explanations, corrections, clarifications
            4. Project Understanding: what the assistant learned about the codebase

            Few-shot examples:

            User: Should I use SQLite or DuckDB?
            Assistant: For vector similarity search with analytical queries, DuckDB is the better fit.
            Output: {"facts": ["Recommended DuckDB over SQLite for vector similarity and analytical queries"]}

            User: Fix this bug please.
            Assistant: Done, the issue was a missing null check.
            Output: {"facts": []}

            User: Hi
            Assistant: Hello! How can I help?
            Output: {"facts": []}

            User: I've been comparing TypeScript and Rust for my next CLI tool. I think I'll go with Rust.
            Assistant: Rust is great for CLIs — single binary, no runtime needed. Have you tried clap for argument parsing?
            Output: {"facts": ["Recommended clap crate for Rust CLI argument parsing"]}

            Guidelines:
            - Today's date is {{dateStr}}.
            - Return JSON with a "facts" key containing an array of strings.
            - If no relevant facts found, return {"facts": []}.
            - Greetings and generic responses are NOT facts.
            - Each fact MUST be self-contained and atomic — understandable without the original conversation.
            - NEVER use pronouns without clear referents. Always name the specific tool, language, or concept.
            - Do NOT extract generic compliments or restatements of user preferences (e.g., "Python 3.10 is solid" is NOT a fact worth storing).
            - Only extract substantive recommendations, technical insights, or new information the assistant contributed.
            - Do NOT extract facts from code the assistant writes or generates. Code belongs in source files, not memory. Only extract recommendations or insights stated in prose.
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
