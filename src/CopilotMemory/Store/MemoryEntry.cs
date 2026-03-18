namespace CopilotMemory.Store;

/// <summary>
/// Represents a memory entry stored in the vector database.
/// Contains the text content, embeddings, metadata, and timestamps.
/// </summary>
public record MemoryEntry
{
    /// <summary>Unique identifier for this memory.</summary>
    public required string Id { get; init; }
    
    /// <summary>The text content of this memory.</summary>
    public required string Text { get; init; }
    
    /// <summary>Source of this memory: "user" or "assistant".</summary>
    public required string Source { get; init; }
    
    /// <summary>Vector embedding of the text (384 dimensions for bge-micro-v2).</summary>
    public required float[] Embedding { get; init; }
    
    /// <summary>Timestamp when this memory was created.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>Timestamp when this memory was last updated.</summary>
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>Optional session identifier for grouping memories.</summary>
    public string? SessionId { get; init; }
}

/// <summary>
/// Result from a semantic similarity search operation.
/// Contains the matched memory with similarity score.
/// </summary>
public record SearchResult
{
    /// <summary>Unique identifier of the matched memory.</summary>
    public required string Id { get; init; }
    
    /// <summary>Text content of the matched memory.</summary>
    public required string Text { get; init; }
    
    /// <summary>Source of the memory: "user" or "assistant".</summary>
    public required string Source { get; init; }
    
    /// <summary>Cosine similarity score (0.0 to 1.0) between query and this memory.</summary>
    public required float Score { get; init; }
    
    /// <summary>Timestamp when this memory was created.</summary>
    public required DateTime CreatedAt { get; init; }
}

/// <summary>
/// Statistics about the memory store.
/// </summary>
public record MemoryStats
{
    /// <summary>Total number of memories stored.</summary>
    public required int Count { get; init; }
    
    /// <summary>Timestamp of the oldest memory, or null if store is empty.</summary>
    public required DateTime? OldestMemory { get; init; }
    
    /// <summary>Timestamp of the newest memory, or null if store is empty.</summary>
    public required DateTime? NewestMemory { get; init; }
}
