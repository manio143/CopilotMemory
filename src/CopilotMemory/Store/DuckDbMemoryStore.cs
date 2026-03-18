using DuckDB.NET.Data;

namespace CopilotMemory.Store;

/// <summary>
/// DuckDB-based vector store for memory entries with cosine similarity search.
/// Stores text, embeddings (FLOAT[384]), and metadata in an embedded analytical database.
/// </summary>
public sealed class DuckDbMemoryStore : IDisposable
{
    private readonly DuckDBConnection _connection;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>
    /// Creates a new DuckDB memory store at the specified path.
    /// </summary>
    /// <param name="dbPath">Path to the DuckDB database file.</param>
    public DuckDbMemoryStore(string dbPath)
    {
        _connection = new DuckDBConnection($"Data Source={dbPath}");
        _connection.Open();
        InitSchema();
    }

    private void InitSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS memories (
                id TEXT PRIMARY KEY,
                text TEXT NOT NULL,
                source TEXT NOT NULL DEFAULT 'user',
                embedding FLOAT[384] NOT NULL,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                session_id TEXT
            );
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Adds a new memory entry to the store.
    /// </summary>
    /// <param name="entry">Memory entry to add.</param>
    public async Task AddAsync(MemoryEntry entry)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO memories (id, text, source, embedding, created_at, updated_at, session_id)
                VALUES ($1, $2, $3, $4::FLOAT[384], $5, $6, $7)
                """;
            cmd.Parameters.Add(new DuckDBParameter { Value = entry.Id });
            cmd.Parameters.Add(new DuckDBParameter { Value = entry.Text });
            cmd.Parameters.Add(new DuckDBParameter { Value = entry.Source });
            cmd.Parameters.Add(new DuckDBParameter { Value = entry.Embedding.ToList() });
            cmd.Parameters.Add(new DuckDBParameter { Value = entry.CreatedAt });
            cmd.Parameters.Add(new DuckDBParameter { Value = entry.UpdatedAt });
            cmd.Parameters.Add(new DuckDBParameter { Value = (object?)entry.SessionId ?? DBNull.Value });
            cmd.ExecuteNonQuery();
        }
        finally { _writeLock.Release(); }
    }

    /// <summary>
    /// Updates an existing memory entry with new text and embedding.
    /// </summary>
    /// <param name="id">ID of the memory to update.</param>
    /// <param name="newText">New text content.</param>
    /// <param name="newEmbedding">New embedding vector.</param>
    public async Task UpdateAsync(string id, string newText, float[] newEmbedding)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                UPDATE memories SET text = $1, embedding = $2::FLOAT[384], updated_at = $3
                WHERE id = $4
                """;
            cmd.Parameters.Add(new DuckDBParameter { Value = newText });
            cmd.Parameters.Add(new DuckDBParameter { Value = newEmbedding.ToList() });
            cmd.Parameters.Add(new DuckDBParameter { Value = DateTime.UtcNow });
            cmd.Parameters.Add(new DuckDBParameter { Value = id });
            cmd.ExecuteNonQuery();
        }
        finally { _writeLock.Release(); }
    }

    /// <summary>
    /// Deletes a memory entry by ID.
    /// </summary>
    /// <param name="id">ID of the memory to delete.</param>
    public async Task DeleteAsync(string id)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM memories WHERE id = $1";
            cmd.Parameters.Add(new DuckDBParameter { Value = id });
            cmd.ExecuteNonQuery();
        }
        finally { _writeLock.Release(); }
    }

    /// <summary>
    /// Searches for memories similar to the query embedding.
    /// </summary>
    /// <param name="queryEmbedding">Query embedding vector.</param>
    /// <param name="limit">Maximum number of results (default: 5).</param>
    /// <param name="minScore">Minimum cosine similarity score (default: 0.3).</param>
    /// <returns>List of matching search results, ordered by similarity score.</returns>
    public List<SearchResult> Search(float[] queryEmbedding, int limit = 5, float minScore = 0.3f)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, text, source, created_at,
                   array_cosine_similarity(embedding, $1::FLOAT[384]) AS score
            FROM memories
            WHERE array_cosine_similarity(embedding, $1::FLOAT[384]) > $2
            ORDER BY score DESC
            LIMIT $3
            """;
        cmd.Parameters.Add(new DuckDBParameter { Value = queryEmbedding.ToList() });
        cmd.Parameters.Add(new DuckDBParameter { Value = minScore });
        cmd.Parameters.Add(new DuckDBParameter { Value = limit });

        var results = new List<SearchResult>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new SearchResult
            {
                Id = reader.GetString(0),
                Text = reader.GetString(1),
                Source = reader.GetString(2),
                Score = reader.GetFloat(4),
                CreatedAt = reader.GetDateTime(3),
            });
        }
        return results;
    }

    /// <summary>
    /// Finds memories similar to the given embedding (used for deduplication).
    /// </summary>
    /// <param name="embedding">Embedding vector to match against.</param>
    /// <param name="threshold">Minimum similarity threshold (default: 0.95).</param>
    /// <returns>List of similar search results.</returns>
    public List<SearchResult> FindSimilar(float[] embedding, float threshold = 0.95f)
    {
        return Search(embedding, limit: 5, minScore: threshold);
    }

    /// <summary>
    /// Gets all memory entries from the store (without embeddings for efficiency).
    /// </summary>
    /// <returns>List of all memory entries.</returns>
    public List<MemoryEntry> GetAll()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, text, source, created_at, updated_at, session_id FROM memories ORDER BY created_at";

        var results = new List<MemoryEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new MemoryEntry
            {
                Id = reader.GetString(0),
                Text = reader.GetString(1),
                Source = reader.GetString(2),
                Embedding = [], // Don't load embeddings for listing
                CreatedAt = reader.GetDateTime(3),
                UpdatedAt = reader.GetDateTime(4),
                SessionId = reader.IsDBNull(5) ? null : reader.GetString(5),
            });
        }
        return results;
    }

    /// <summary>
    /// Gets statistics about the memory store (count, oldest, newest).
    /// </summary>
    /// <returns>Memory statistics.</returns>
    public MemoryStats GetStats()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*), MIN(created_at), MAX(created_at) FROM memories";
        using var reader = cmd.ExecuteReader();
        reader.Read();
        var count = Convert.ToInt32(reader.GetValue(0));
        return new MemoryStats
        {
            Count = count,
            OldestMemory = count > 0 ? reader.GetDateTime(1) : null,
            NewestMemory = count > 0 ? reader.GetDateTime(2) : null,
        };
    }

    /// <summary>
    /// Disposes the DuckDB connection.
    /// </summary>
    public void Dispose()
    {
        _connection.Dispose();
        _writeLock.Dispose();
    }
}
