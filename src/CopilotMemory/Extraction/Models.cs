namespace CopilotMemory.Extraction;

internal record Fact
{
    public required string Text { get; init; }
    public required string Source { get; init; } // "user" | "assistant"
}

internal enum UpdateEvent
{
    ADD,
    UPDATE,
    DELETE,
    NONE,
}

internal record UpdateDecision
{
    public required string Id { get; init; }
    public required string Text { get; init; }
    public required UpdateEvent Event { get; init; }
    public string? OldMemory { get; init; }
}
