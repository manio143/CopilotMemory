using DuckDB.NET.Data;

namespace CopilotMemory.Store;

internal sealed class DuckDbMemoryStore : IDisposable
{
    private readonly DuckDBConnection _connection;
    private readonly object _writeLock = new();

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

    public void Add(MemoryEntry entry)
    {
        lock (_writeLock)
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
    }

    public void Update(string id, string newText, float[] newEmbedding)
    {
        lock (_writeLock)
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
    }

    public void Delete(string id)
    {
        lock (_writeLock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM memories WHERE id = $1";
            cmd.Parameters.Add(new DuckDBParameter { Value = id });
            cmd.ExecuteNonQuery();
        }
    }

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

    public List<SearchResult> FindSimilar(float[] embedding, float threshold = 0.95f)
    {
        return Search(embedding, limit: 5, minScore: threshold);
    }

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

    public void Dispose() => _connection.Dispose();
}
