using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SourcingEngine.Core.Configuration;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Embedding service using Ollama's local API with nomic-embed-text model.
/// Produces 768-dimensional embeddings with superior semantic understanding.
/// </summary>
public class OllamaEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<OllamaEmbeddingService> _logger;
    private readonly OllamaSettings _settings;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(24);
    private readonly JsonSerializerOptions _jsonOptions;

    public int EmbeddingDimension => _settings.EmbeddingDimension;

    public OllamaEmbeddingService(
        HttpClient httpClient,
        IMemoryCache cache,
        ILogger<OllamaEmbeddingService> logger,
        IOptions<OllamaSettings> settings)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));

        _httpClient.BaseAddress = new Uri(_settings.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.EmbeddingTimeoutSeconds);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text cannot be null or whitespace", nameof(text));
        }

        var cacheKey = $"ollama_embedding:{_settings.EmbeddingModel}:{EmbeddingUtilities.ComputeHash(text)}";

        if (_cache.TryGetValue(cacheKey, out float[]? cachedEmbedding) && cachedEmbedding != null)
        {
            _logger.LogDebug("Embedding cache hit for: {Text}", EmbeddingUtilities.TruncateForLog(text));
            return cachedEmbedding;
        }

        _logger.LogDebug("Generating Ollama embedding for: {Text}", EmbeddingUtilities.TruncateForLog(text));

        try
        {
            var request = new OllamaEmbeddingRequest
            {
                Model = _settings.EmbeddingModel,
                Prompt = text
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/api/embeddings",
                request,
                _jsonOptions,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(
                _jsonOptions,
                cancellationToken);

            if (result?.Embedding == null || result.Embedding.Length == 0)
            {
                throw new InvalidOperationException("Ollama returned empty embedding");
            }

            // Convert double[] to float[]
            var embedding = result.Embedding.Select(d => (float)d).ToArray();

            if (embedding.Length != _settings.EmbeddingDimension)
            {
                _logger.LogWarning(
                    "Embedding dimension mismatch: expected {Expected}, got {Actual}",
                    _settings.EmbeddingDimension,
                    embedding.Length);
            }

            _cache.Set(cacheKey, embedding, _cacheExpiration);

            return embedding;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "Failed to connect to Ollama at {BaseUrl}. Ensure Ollama is running with: ollama serve",
                _settings.BaseUrl);
            throw new InvalidOperationException(
                $"Failed to connect to Ollama at {_settings.BaseUrl}. Ensure Ollama is running.", ex);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex,
                "Ollama embedding request timed out after {Timeout}s",
                _settings.EmbeddingTimeoutSeconds);
            throw;
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

        _logger.LogInformation("Generating embeddings for {Count} texts", textList.Count);

        int processed = 0;
        foreach (var text in textList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var embedding = await GenerateEmbeddingAsync(text, cancellationToken);
            results.Add(embedding);

            processed++;
            if (processed % 10 == 0)
            {
                _logger.LogDebug("Processed {Processed}/{Total} embeddings", processed, textList.Count);
            }
        }

        return results;
    }

    /// <summary>
    /// Check if Ollama service is available
    /// </summary>
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        return OllamaHealthCheck.IsModelAvailableAsync(_httpClient, _settings.EmbeddingModel, cancellationToken);
    }

    /// <summary>
    /// Check if the configured embedding model is available
    /// </summary>
    public Task<bool> IsModelAvailableAsync(CancellationToken cancellationToken = default)
    {
        return OllamaHealthCheck.IsModelAvailableAsync(_httpClient, _settings.EmbeddingModel, cancellationToken);
    }
}

/// <summary>
/// Request payload for Ollama embeddings API
/// </summary>
internal class OllamaEmbeddingRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; set; }
}

/// <summary>
/// Response payload from Ollama embeddings API
/// </summary>
internal class OllamaEmbeddingResponse
{
    [JsonPropertyName("embedding")]
    public double[]? Embedding { get; set; }
}
