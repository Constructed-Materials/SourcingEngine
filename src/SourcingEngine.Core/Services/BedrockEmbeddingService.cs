using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SourcingEngine.Core.Configuration;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Embedding service using AWS Bedrock with Amazon Titan Text Embeddings V2.
/// Produces configurable-dimension embeddings (256/512/1024) for cloud deployments
/// where Ollama is not available.
/// Uses the default AWS credential chain (IAM Role on ECS/Lambda, env vars, or profile).
/// </summary>
public class BedrockEmbeddingService : IEmbeddingService, IDisposable
{
    private readonly IAmazonBedrockRuntime _client;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BedrockEmbeddingService> _logger;
    private readonly BedrockSettings _settings;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(24);
    private readonly SemaphoreSlim _throttle;
    private readonly bool _ownsClient;

    public int EmbeddingDimension => _settings.EmbeddingDimension;

    public BedrockEmbeddingService(
        IMemoryCache cache,
        ILogger<BedrockEmbeddingService> logger,
        IOptions<BedrockSettings> settings)
        : this(null, cache, logger, settings)
    {
    }

    /// <summary>
    /// Constructor with injectable IAmazonBedrockRuntime for testing.
    /// </summary>
    public BedrockEmbeddingService(
        IAmazonBedrockRuntime? client,
        IMemoryCache cache,
        ILogger<BedrockEmbeddingService> logger,
        IOptions<BedrockSettings> settings)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _throttle = new SemaphoreSlim(_settings.MaxConcurrentEmbeddings);

        if (client != null)
        {
            _client = client;
            _ownsClient = false;
        }
        else
        {
            _client = new AmazonBedrockRuntimeClient(RegionEndpoint.GetBySystemName(_settings.Region));
            _ownsClient = true;
        }
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text cannot be null or whitespace", nameof(text));
        }

        var cacheKey = $"bedrock_embedding:{_settings.EmbeddingModelId}:{_settings.EmbeddingDimension}:{EmbeddingUtilities.ComputeHash(text)}";

        if (_cache.TryGetValue(cacheKey, out float[]? cachedEmbedding) && cachedEmbedding != null)
        {
            _logger.LogDebug("Embedding cache hit for: {Text}", EmbeddingUtilities.TruncateForLog(text));
            return cachedEmbedding;
        }

        _logger.LogDebug("Generating Bedrock embedding for: {Text}", EmbeddingUtilities.TruncateForLog(text));

        try
        {
            var requestBody = new TitanEmbeddingRequest
            {
                InputText = text,
                Dimensions = _settings.EmbeddingDimension,
                Normalize = _settings.EmbeddingNormalize
            };

            var jsonBody = JsonSerializer.Serialize(requestBody, TitanJsonContext.Default.TitanEmbeddingRequest);

            var request = new InvokeModelRequest
            {
                ModelId = _settings.EmbeddingModelId,
                ContentType = "application/json",
                Accept = "application/json",
                Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonBody))
            };

            var response = await _client.InvokeModelAsync(request, cancellationToken);

            using var responseStream = response.Body;
            var responseBody = await JsonSerializer.DeserializeAsync<TitanEmbeddingResponse>(
                responseStream, TitanJsonContext.Default.TitanEmbeddingResponse, cancellationToken);

            if (responseBody?.Embedding == null || responseBody.Embedding.Length == 0)
            {
                throw new InvalidOperationException("Bedrock returned empty embedding");
            }

            var embedding = responseBody.Embedding;

            if (embedding.Length != _settings.EmbeddingDimension)
            {
                _logger.LogWarning(
                    "Embedding dimension mismatch: expected {Expected}, got {Actual}",
                    _settings.EmbeddingDimension,
                    embedding.Length);
            }

            _cache.Set(cacheKey, embedding, _cacheExpiration);

            _logger.LogDebug(
                "Generated {Dim}-dim embedding ({Tokens} tokens) for: {Text}",
                embedding.Length,
                responseBody.InputTextTokenCount,
                EmbeddingUtilities.TruncateForLog(text));

            return embedding;
        }
        catch (Amazon.BedrockRuntime.Model.ValidationException ex)
        {
            _logger.LogError(ex, "Bedrock validation error for model {ModelId}", _settings.EmbeddingModelId);
            throw new InvalidOperationException(
                $"Bedrock validation error: {ex.Message}. Check model ID and dimensions.", ex);
        }
        catch (Amazon.BedrockRuntime.Model.ThrottlingException ex)
        {
            _logger.LogWarning(ex, "Bedrock throttling — consider reducing MaxConcurrentEmbeddings (current: {Max})",
                _settings.MaxConcurrentEmbeddings);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to generate Bedrock embedding for text: {Text}",
                EmbeddingUtilities.TruncateForLog(text));
            throw;
        }
    }

    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        var results = new float[textList.Count][];

        _logger.LogInformation(
            "Generating Bedrock embeddings for {Count} texts (concurrency: {Max})",
            textList.Count, _settings.MaxConcurrentEmbeddings);

        // Use SemaphoreSlim to limit concurrent Bedrock API calls and avoid throttling
        var tasks = textList.Select(async (text, index) =>
        {
            await _throttle.WaitAsync(cancellationToken);
            try
            {
                results[index] = await GenerateEmbeddingAsync(text, cancellationToken);
            }
            finally
            {
                _throttle.Release();
            }
        });

        await Task.WhenAll(tasks);

        _logger.LogInformation("Completed {Count} Bedrock embeddings", textList.Count);
        return results;
    }

    public void Dispose()
    {
        _throttle.Dispose();
        if (_ownsClient && _client is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

// ─── Titan Embedding V2 request/response DTOs ───

internal class TitanEmbeddingRequest
{
    [JsonPropertyName("inputText")]
    public required string InputText { get; set; }

    [JsonPropertyName("dimensions")]
    public int Dimensions { get; set; } = 1024;

    [JsonPropertyName("normalize")]
    public bool Normalize { get; set; } = true;
}

internal class TitanEmbeddingResponse
{
    [JsonPropertyName("embedding")]
    public float[]? Embedding { get; set; }

    [JsonPropertyName("inputTextTokenCount")]
    public int InputTextTokenCount { get; set; }
}

/// <summary>
/// Source-generated JSON serializer context for Titan embedding DTOs.
/// Avoids reflection-based serialization overhead on AOT/trimmed deployments.
/// </summary>
[JsonSerializable(typeof(TitanEmbeddingRequest))]
[JsonSerializable(typeof(TitanEmbeddingResponse))]
internal partial class TitanJsonContext : JsonSerializerContext
{
}
