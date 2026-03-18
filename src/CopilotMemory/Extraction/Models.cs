namespace CopilotMemory.Extraction;

/// <summary>
/// Represents an extracted fact from a conversation.
/// </summary>
public record Fact
{
    /// <summary>Text content of the fact.</summary>
    public required string Text { get; init; }
    
    /// <summary>Source of the fact: "user" or "assistant".</summary>
    public required string Source { get; init; }
}

/// <summary>
/// Update decision event types for deduplication.
/// </summary>
public enum UpdateEvent
{
    /// <summary>Add as a new memory.</summary>
    ADD,
    
    /// <summary>Update an existing memory with new information.</summary>
    UPDATE,
    
    /// <summary>Delete an existing memory (contradicted by new fact).</summary>
    DELETE,
    
    /// <summary>No action needed (duplicate or irrelevant).</summary>
    NONE,
}

/// <summary>
/// Represents a decision about how to handle a fact against existing memories.
/// </summary>
public record UpdateDecision
{
    /// <summary>Memory ID (existing or new).</summary>
    public required string Id { get; init; }
    
    /// <summary>Memory text content.</summary>
    public required string Text { get; init; }
    
    /// <summary>Update event type (ADD/UPDATE/DELETE/NONE).</summary>
    public required UpdateEvent Event { get; init; }
    
    /// <summary>Original memory text (only for UPDATE events).</summary>
    public string? OldMemory { get; init; }
}
