using ElBruno.LocalEmbeddings;
using ElBruno.LocalEmbeddings.Options;

namespace CopilotMemory.Embeddings;

/// <summary>
/// Local embedding service using ElBruno.LocalEmbeddings with ONNX Runtime.
///
/// Switched from SmartComponents.LocalEmbeddings because:
/// 1. SmartComponents only ships one model (bge-micro-v2), no alternatives
/// 2. ElBruno supports any HuggingFace sentence-transformer model with auto-download
/// 3. Implements Microsoft.Extensions.AI IEmbeddingGenerator interface
/// 4. Actively maintained (Feb 2026+) by Microsoft MVP El Bruno
/// 5. Async API, proper model caching, better DI support
///
/// Default model: SmartComponents/bge-micro-v2 (384 dimensions).
/// bge-micro-v2 outperforms all-MiniLM-L6-v2 for short fact embeddings (~0.2 higher scores).
/// Configurable via constructor parameter for users who want a different model.
/// </summary>
public sealed class LocalEmbeddingService : IAsyncDisposable, IDisposable
{
    private LocalEmbeddingGenerator? _generator;
    private readonly string _modelName;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    /// <summary>
    /// Creates a new local embedding service with the specified model.
    /// </summary>
    /// <param name="modelName">HuggingFace model name (default: SmartComponents/bge-micro-v2).</param>
    public LocalEmbeddingService(string modelName = "SmartComponents/bge-micro-v2")
    {
        _modelName = modelName;
    }

    private async Task<LocalEmbeddingGenerator> GetGeneratorAsync()
    {
        if (_generator != null) return _generator;

        await _initLock.WaitAsync();
        try
        {
            _generator ??= await LocalEmbeddingGenerator.CreateAsync(new LocalEmbeddingsOptions
            {
                ModelName = _modelName,
                EnsureModelDownloaded = true,
            });
            return _generator;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Generates a vector embedding for the given text asynchronously.
    /// </summary>
    /// <param name="text">Text to embed.</param>
    /// <returns>384-dimensional embedding vector.</returns>
    public async Task<float[]> EmbedAsync(string text)
    {
        var gen = await GetGeneratorAsync();
        var embeddings = await gen.GenerateAsync([text]);
        return embeddings[0].Vector.ToArray();
    }

    /// <summary>
    /// Synchronous embed for backward compatibility. Blocks on async.
    /// Prefer EmbedAsync when possible.
    /// </summary>
    /// <param name="text">Text to embed.</param>
    /// <returns>384-dimensional embedding vector.</returns>
    public float[] Embed(string text)
    {
        return EmbedAsync(text).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Calculates cosine similarity between two embedding vectors.
    /// </summary>
    /// <param name="a">First embedding vector.</param>
    /// <param name="b">Second embedding vector.</param>
    /// <returns>Cosine similarity score (0.0 to 1.0).</returns>
    public float CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        return (float)(dot / (Math.Sqrt(magA) * Math.Sqrt(magB)));
    }

    public void Dispose()
    {
        _generator?.Dispose();
        _initLock.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_generator != null)
            await _generator.DisposeAsync();
        _initLock.Dispose();
    }
}
