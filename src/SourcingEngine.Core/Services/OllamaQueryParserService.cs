using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SourcingEngine.Core.Configuration;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Query parsing service using Ollama's local LLM (llama3.2:3b) for BOM line item analysis.
/// Extracts material family, dimensions, and attributes from free-form construction BOM text.
/// </summary>
public class OllamaQueryParserService : IQueryParserService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaQueryParserService> _logger;
    private readonly OllamaSettings _settings;
    private readonly JsonSerializerOptions _jsonOptions;

    private const string SystemPrompt = @"You are a construction materials parser. Extract structured data from BOM (Bill of Materials) line items.

TASK: Parse the input and return JSON with these fields:
- material_family: lowercase identifier (cmu, floor_joist, stucco, rebar, lumber, etc.)
- width_inches: width dimension in inches (null if not specified)
- height_inches: height dimension in inches (null if not specified)  
- length_inches: length dimension in inches (null if not specified)
- thickness_inches: thickness in inches (null if not specified)
- diameter_inches: diameter in inches for round items (null if not specified)
- attributes: object with color, grade, finish, type, etc.
- search_query: COMPREHENSIVE search string (see rules below)
- confidence: 0.0-1.0 confidence score

SEARCH_QUERY RULES (CRITICAL):
The search_query MUST be as comprehensive as possible to maximize matching. Include ALL of:
1. ALL size variants with ROUNDED metric/imperial conversions
2. ALL common industry synonyms and alternate names for the material
3. The original terms plus expanded terms

DIMENSION CONVERSION TABLE (use ROUNDED practical values):
- 1 inch = 25 mm (rounded to nearest 5 or 10mm for common sizes)
- 1 inch = 2.5 cm (rounded)
- 1 ft = 0.3 m (rounded), 1 m = 3.3 ft (rounded)
- 1 sqft = 0.09 sqm (rounded), 1 sqm = 10.8 sqft (rounded)
- Standard CMU: 8x8x16 inches = 200x200x400 mm = 20x20x40 cm
- Rebar: #3=0.375in=10mm, #4=0.5in=13mm, #5=0.625in=16mm, #6=0.75in=19mm, #8=1.0in=25mm

SYNONYM RULES:
- CMU = concrete masonry unit = concrete block = masonry block = masonry unit
- Stucco = EIFS = exterior insulation
- Joist = i-joist = floor joist = engineered joist
- Railing = handrail = guardrail = balustrade
- LVL = laminated veneer lumber, LSL = laminated strand lumber
- Lumber = wood = timber
- Aluminum = aluminium

EXAMPLES:";

    private const string FewShotExamples = @"
INPUT: ""8 inch concrete masonry unit gray""
OUTPUT: {""material_family"":""cmu"",""width_inches"":8,""height_inches"":8,""length_inches"":16,""thickness_inches"":null,""diameter_inches"":null,""attributes"":{""color"":""gray""},""search_query"":""8 inch 20 cm 200 mm 8x8x16 concrete masonry unit CMU concrete block masonry block masonry unit gray"",""confidence"":0.95}

INPUT: ""2x10 floor joist 12ft pressure treated""
OUTPUT: {""material_family"":""floor_joist"",""width_inches"":1.5,""height_inches"":9.25,""length_inches"":144,""thickness_inches"":null,""diameter_inches"":null,""attributes"":{""treatment"":""pressure treated"",""nominal"":""2x10""},""search_query"":""2x10 floor joist i-joist engineered joist 12 ft 4 m lumber wood pressure treated"",""confidence"":0.92}

INPUT: ""#5 rebar grade 60""
OUTPUT: {""material_family"":""rebar"",""width_inches"":null,""height_inches"":null,""length_inches"":null,""thickness_inches"":null,""diameter_inches"":0.625,""attributes"":{""grade"":""60"",""size"":""#5""},""search_query"":""#5 rebar reinforcing bar grade 60 0.625 inch 16 mm diameter steel"",""confidence"":0.98}

INPUT: ""200mm masonry block lightweight""
OUTPUT: {""material_family"":""cmu"",""width_inches"":7.87,""height_inches"":7.87,""length_inches"":15.75,""thickness_inches"":null,""diameter_inches"":null,""attributes"":{""weight"":""lightweight""},""search_query"":""200 mm 20 cm 8 inch concrete masonry unit CMU concrete block masonry block masonry unit lightweight"",""confidence"":0.88}

Now parse this input and return ONLY valid JSON (no explanation):";

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
            var prompt = $"{SystemPrompt}{FewShotExamples}\nINPUT: \"{bomLineItem}\"\nOUTPUT:";

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
            return ParseLlmResponse(result.Response, bomLineItem);
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

    private ParsedBomQuery ParseLlmResponse(string llmOutput, string originalInput)
    {
        try
        {
            // Extract JSON from response (LLM might include extra text)
            // Try greedy match FIRST — handles nested objects like "attributes":{"color":"gray"}
            var jsonMatch = Regex.Match(llmOutput, @"\{.*\}", RegexOptions.Singleline);
            if (!jsonMatch.Success)
            {
                // Fallback: try non-nested match for simple JSON without braces
                jsonMatch = Regex.Match(llmOutput, @"\{[^{}]*\}", RegexOptions.Singleline);
            }

            if (!jsonMatch.Success)
            {
                _logger.LogWarning("Could not extract JSON from LLM response: {Response}", llmOutput);
                return new ParsedBomQuery
                {
                    Success = false,
                    ErrorMessage = "Could not parse LLM response as JSON",
                    OriginalInput = originalInput,
                    SearchQuery = originalInput // Fallback to original input
                };
            }

            var json = jsonMatch.Value;
            var parsed = JsonSerializer.Deserialize<LlmParseResult>(json, _jsonOptions);

            if (parsed == null)
            {
                return new ParsedBomQuery
                {
                    Success = false,
                    ErrorMessage = "Parsed JSON was null",
                    OriginalInput = originalInput,
                    SearchQuery = originalInput
                };
            }

            // Validation gate: if both MaterialFamily and SearchQuery are null,
            // the regex likely captured a nested fragment (e.g. {"color":"gray"})
            if (string.IsNullOrWhiteSpace(parsed.MaterialFamily) && string.IsNullOrWhiteSpace(parsed.SearchQuery))
            {
                _logger.LogWarning(
                    "LLM response deserialized but missing both MaterialFamily and SearchQuery. " +
                    "Extracted JSON: {Json}", json);
                return new ParsedBomQuery
                {
                    Success = false,
                    ErrorMessage = "Parsed JSON missing required fields (material_family and search_query)",
                    OriginalInput = originalInput,
                    SearchQuery = originalInput
                };
            }

            return new ParsedBomQuery
            {
                Success = true,
                OriginalInput = originalInput,
                MaterialFamily = parsed.MaterialFamily,
                TechnicalSpecs = new TechnicalSpecs
                {
                    WidthInches = parsed.WidthInches,
                    HeightInches = parsed.HeightInches,
                    LengthInches = parsed.LengthInches,
                    ThicknessInches = parsed.ThicknessInches,
                    DiameterInches = parsed.DiameterInches
                },
                Attributes = parsed.Attributes ?? new Dictionary<string, string>(),
                SearchQuery = parsed.SearchQuery ?? originalInput,
                Confidence = parsed.Confidence ?? 0.5f
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize LLM response: {Response}", llmOutput);
            return new ParsedBomQuery
            {
                Success = false,
                ErrorMessage = $"JSON parse error: {ex.Message}",
                OriginalInput = originalInput,
                SearchQuery = originalInput
            };
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/tags", cancellationToken);
            if (!response.IsSuccessStatusCode) return false;

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return content.Contains(_settings.ParsingModel, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Internal class for deserializing LLM JSON output
/// </summary>
internal class LlmParseResult
{
    [JsonPropertyName("material_family")]
    public string? MaterialFamily { get; set; }

    [JsonPropertyName("width_inches")]
    public double? WidthInches { get; set; }

    [JsonPropertyName("height_inches")]
    public double? HeightInches { get; set; }

    [JsonPropertyName("length_inches")]
    public double? LengthInches { get; set; }

    [JsonPropertyName("thickness_inches")]
    public double? ThicknessInches { get; set; }

    [JsonPropertyName("diameter_inches")]
    public double? DiameterInches { get; set; }

    [JsonPropertyName("attributes")]
    public Dictionary<string, string>? Attributes { get; set; }

    [JsonPropertyName("search_query")]
    public string? SearchQuery { get; set; }

    [JsonPropertyName("confidence")]
    public float? Confidence { get; set; }
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
