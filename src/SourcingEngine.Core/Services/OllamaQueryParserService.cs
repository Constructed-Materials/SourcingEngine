using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SourcingEngine.Core.Configuration;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Query parsing service using Ollama's local LLM (llama3.2:3b) for BOM line item analysis.
/// Extracts material family, dimensions, and attributes from free-form construction BOM text.
/// Uses shared QueryParserPrompts and QueryParserResponseParser for consistency with other providers.
/// </summary>
public class OllamaQueryParserService : IQueryParserService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaQueryParserService> _logger;
    private readonly OllamaSettings _settings;
    private readonly JsonSerializerOptions _jsonOptions;

    public OllamaQueryParserService(
        HttpClient httpClient,
        ILogger<OllamaQueryParserService> logger,
        IOptions<OllamaSettings> settings)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));

        _httpClient.BaseAddress = new Uri(_settings.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.ParsingTimeoutSeconds);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<ParsedBomQuery> ParseAsync(string bomLineItem, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bomLineItem))
        {
            return new ParsedBomQuery
            {
                Success = false,
                ErrorMessage = "Input cannot be null or empty",
                OriginalInput = bomLineItem ?? string.Empty
            };
        }

        _logger.LogDebug("Parsing BOM line item: {Input}", bomLineItem);

        try
        {
            var prompt = QueryParserPrompts.BuildOllamaPrompt(bomLineItem);

            var request = new OllamaGenerateRequest
            {
                Model = _settings.ParsingModel,
                Prompt = prompt,
                Stream = false,
                Options = new OllamaGenerateOptions
                {
                    Temperature = 0.1f,  // Low temperature for deterministic output
                    NumPredict = 500     // Max tokens
                }
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/api/generate",
                request,
                _jsonOptions,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(
                _jsonOptions,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(result?.Response))
            {
                return new ParsedBomQuery
                {
                    Success = false,
                    ErrorMessage = "LLM returned empty response",
                    OriginalInput = bomLineItem
                };
            }

            _logger.LogDebug("Raw LLM response: {Response}", result.Response);
            return QueryParserResponseParser.Parse(result.Response, bomLineItem, _logger);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to connect to Ollama for parsing");
            return new ParsedBomQuery
            {
                Success = false,
                ErrorMessage = $"Failed to connect to Ollama: {ex.Message}",
                OriginalInput = bomLineItem
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse BOM line item: {Input}", bomLineItem);
            return new ParsedBomQuery
            {
                Success = false,
                ErrorMessage = ex.Message,
                OriginalInput = bomLineItem
            };
        }
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        return OllamaHealthCheck.IsModelAvailableAsync(_httpClient, _settings.ParsingModel, cancellationToken);
    }
}

/// <summary>
/// Request payload for Ollama generate API
/// </summary>
internal class OllamaGenerateRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = false;

    [JsonPropertyName("options")]
    public OllamaGenerateOptions? Options { get; set; }
}

internal class OllamaGenerateOptions
{
    [JsonPropertyName("temperature")]
    public float Temperature { get; set; } = 0.1f;

    [JsonPropertyName("num_predict")]
    public int NumPredict { get; set; } = 500;
}

/// <summary>
/// Response payload from Ollama generate API
/// </summary>
internal class OllamaGenerateResponse
{
    [JsonPropertyName("response")]
    public string? Response { get; set; }

    [JsonPropertyName("done")]
    public bool Done { get; set; }
}
