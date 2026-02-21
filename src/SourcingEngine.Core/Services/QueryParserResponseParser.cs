using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Shared JSON response parser for BOM query parsing LLM output.
/// Used by both OllamaQueryParserService and BedrockQueryParserService.
/// Extracts JSON from raw LLM text, deserializes into ParsedBomQuery.
/// </summary>
public static class QueryParserResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Parse raw LLM text output into a structured ParsedBomQuery.
    /// Handles JSON extraction from wrapped text, validation, and error cases.
    /// </summary>
    public static ParsedBomQuery Parse(string llmOutput, string originalInput, ILogger? logger = null)
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
                logger?.LogWarning("Could not extract JSON from LLM response: {Response}", llmOutput);
                return new ParsedBomQuery
                {
                    Success = false,
                    ErrorMessage = "Could not parse LLM response as JSON",
                    OriginalInput = originalInput,
                    SearchQuery = originalInput // Fallback to original input
                };
            }

            var json = jsonMatch.Value;
            var parsed = JsonSerializer.Deserialize<LlmParseResult>(json, JsonOptions);

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
                logger?.LogWarning(
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
            logger?.LogWarning(ex, "Failed to deserialize LLM response: {Response}", llmOutput);
            return new ParsedBomQuery
            {
                Success = false,
                ErrorMessage = $"JSON parse error: {ex.Message}",
                OriginalInput = originalInput,
                SearchQuery = originalInput
            };
        }
    }
}

/// <summary>
/// Internal DTO for deserializing LLM JSON output (snake_case).
/// Shared between all query parser implementations.
/// </summary>
public class LlmParseResult
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
