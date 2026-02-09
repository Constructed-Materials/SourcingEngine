using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SmartComponents.LocalEmbeddings;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Embedding service using SmartComponents.LocalEmbeddings (bge-micro-v2 model).
/// Zero-cost, offline-capable, 384-dimensional embeddings.
/// </summary>
public class LocalEmbeddingService : IEmbeddingService, IDisposable
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<LocalEmbeddingService> _logger;
    private readonly Lazy<LocalEmbedder> _embedder;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(24);
    private bool _disposed;

    /// <summary>
    /// The bge-micro-v2 model produces 384-dimensional embeddings
    /// </summary>
    public int EmbeddingDimension => 384;

    public LocalEmbeddingService(IMemoryCache cache, ILogger<LocalEmbeddingService> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Lazy initialization - model is downloaded on first use (~17MB)
        _embedder = new Lazy<LocalEmbedder>(() =>
        {
            _logger.LogInformation("Initializing local embedding model (bge-micro-v2)...");
            var embedder = new LocalEmbedder();
            _logger.LogInformation("Local embedding model initialized successfully");
            return embedder;
        });
    }

    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text cannot be null or whitespace", nameof(text));
        }

        var cacheKey = $"embedding:{EmbeddingUtilities.ComputeHash(text)}";
        
        if (_cache.TryGetValue(cacheKey, out float[]? cachedEmbedding) && cachedEmbedding != null)
        {
            _logger.LogDebug("Embedding cache hit for: {Text}", EmbeddingUtilities.TruncateForLog(text));
            return Task.FromResult(cachedEmbedding);
        }

        _logger.LogDebug("Generating embedding for: {Text}", EmbeddingUtilities.TruncateForLog(text));
        
        try
        {
            var embedding = _embedder.Value.Embed(text);
            var embeddingArray = embedding.Values.ToArray();
            
            _cache.Set(cacheKey, embeddingArray, _cacheExpiration);
            
            return Task.FromResult(embeddingArray);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate embedding for text: {Text}", EmbeddingUtilities.TruncateForLog(text));
            throw;
        }
    }

    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        var results = new List<float[]>(textList.Count);
        
        foreach (var text in textList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var embedding = await GenerateEmbeddingAsync(text, cancellationToken);
            results.Add(embedding);
        }
        
        return results;
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        if (_embedder.IsValueCreated)
        {
            _embedder.Value.Dispose();
        }
        
        _disposed = true;
    }
}
