using System.Text.Json;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SourcingEngine.Common.Models;
using SourcingEngine.Core.Configuration;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Enriches embedding text by calling AWS Bedrock Converse API to produce fluent
/// natural-language [DESCRIPTION] and [PRODUCTENRICHMENT] sections.
/// Falls back to deterministic text concatenation if the LLM call fails.
/// </summary>
public class BedrockEmbeddingTextEnricher : IEmbeddingTextEnricher, IDisposable
{
    private readonly IAmazonBedrockRuntime _client;
    private readonly ILogger<BedrockEmbeddingTextEnricher> _logger;
    private readonly BedrockSettings _settings;
    private readonly bool _ownsClient;

    public BedrockEmbeddingTextEnricher(
        ILogger<BedrockEmbeddingTextEnricher> logger,
        IOptions<BedrockSettings> settings)
        : this(null, logger, settings)
    {
    }

    /// <summary>
    /// Constructor with injectable IAmazonBedrockRuntime for testing.
    /// </summary>
    public BedrockEmbeddingTextEnricher(
        IAmazonBedrockRuntime? client,
        ILogger<BedrockEmbeddingTextEnricher> logger,
        IOptions<BedrockSettings> settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));

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

    public async Task<EnrichedEmbeddingText> EnrichProductTextAsync(
        ProductEmbeddingInput product,
        CancellationToken cancellationToken = default)
    {
        var userPrompt = BuildProductPrompt(product);

        try
        {
            var result = await CallBedrockAsync(userPrompt, cancellationToken);
            if (result != null)
                return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "LLM enrichment failed for product {ProductId}, falling back to deterministic text",
                product.ProductId);
        }

        // Fallback: deterministic concatenation
        return BuildProductFallback(product);
    }

    /// <inheritdoc />
    /// <remarks>
    /// For BOM items the LLM only produces <c>description</c> and <c>enrichment</c>.
    /// Technical specs and certifications are taken directly from the <see cref="BomLineItem"/>.
    /// </remarks>
    public async Task<EnrichedEmbeddingText> EnrichBomItemTextAsync(
        BomLineItem bomItem,
        ParsedBomQuery parsedQuery,
        CancellationToken cancellationToken = default)
    {
        var userPrompt = BuildBomItemPrompt(bomItem, parsedQuery);

        try
        {
            var result = await CallBedrockForBomAsync(userPrompt, cancellationToken);
            if (result != null)
                return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "LLM enrichment failed for BOM item '{BomItem}', falling back to deterministic text",
                bomItem.BomItem);
        }

        // Fallback: deterministic concatenation
        return BuildBomItemFallback(bomItem, parsedQuery);
    }

    private async Task<EnrichedEmbeddingText?> CallBedrockAsync(
        string userPrompt, CancellationToken cancellationToken)
    {
        var request = new ConverseRequest
        {
            ModelId = _settings.ParsingModelId,
            System = new List<SystemContentBlock>
            {
                new() { Text = EmbeddingTextEnricherPrompts.SystemPrompt }
            },
            Messages = new List<Message>
            {
                new()
                {
                    Role = ConversationRole.User,
                    Content = new List<ContentBlock>
                    {
                        new() { Text = userPrompt }
                    }
                }
            },
            InferenceConfig = new InferenceConfiguration
            {
                MaxTokens = _settings.ParsingMaxTokens,
                Temperature = _settings.ParsingTemperature
            }
        };

        var response = await _client.ConverseAsync(request, cancellationToken);
        var outputText = response.Output?.Message?.Content?.FirstOrDefault()?.Text;

        if (string.IsNullOrWhiteSpace(outputText))
        {
            _logger.LogWarning("Bedrock returned empty enrichment response");
            return null;
        }

        _logger.LogDebug("Bedrock enrichment response ({InputTokens}in/{OutputTokens}out): {Response}",
            response.Usage?.InputTokens, response.Usage?.OutputTokens, outputText);

        return ParseEnrichmentResponse(outputText);
    }

    private EnrichedEmbeddingText? ParseEnrichmentResponse(string responseText)
    {
        try
        {
            // Strip markdown code fences if the LLM wrapped the JSON
            var json = responseText.Trim();
            if (json.StartsWith("```"))
            {
                var firstNewline = json.IndexOf('\n');
                if (firstNewline > 0)
                    json = json.Substring(firstNewline + 1);
                if (json.EndsWith("```"))
                    json = json.Substring(0, json.Length - 3);
                json = json.Trim();
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Parse technical_specs as a JSON array of {name, value, uom} objects
            var specs = new List<TechnicalSpecItem>();
            if (root.TryGetProperty("technical_specs", out var specsElement))
            {
                if (specsElement.ValueKind == JsonValueKind.Array)
                {
                    specs = ParseTechnicalSpecsArray(specsElement);
                }
                // Backwards-compat: if LLM still returns a string, ignore it (fallback will handle)
            }

            return new EnrichedEmbeddingText
            {
                Description = root.TryGetProperty("description", out var desc)
                    ? desc.GetString() ?? string.Empty
                    : string.Empty,
                TechnicalSpecs = specs,
                Enrichment = root.TryGetProperty("enrichment", out var enrich)
                    ? enrich.GetString() ?? string.Empty
                    : string.Empty
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM enrichment JSON: {Response}", responseText);
            return null;
        }
    }

    /// <summary>
    /// Sends a user prompt to AWS Bedrock with the BOM-specific system prompt
    /// and parses the simplified 2-field response (description + enrichment only).
    /// Returns <see langword="null"/> on empty response so callers fall back to deterministic text.
    /// </summary>
    private async Task<EnrichedEmbeddingText?> CallBedrockForBomAsync(
        string userPrompt, CancellationToken cancellationToken)
    {
        var request = new ConverseRequest
        {
            ModelId = _settings.ParsingModelId,
            System = new List<SystemContentBlock>
            {
                new() { Text = EmbeddingTextEnricherPrompts.BomItemSystemPrompt }
            },
            Messages = new List<Message>
            {
                new()
                {
                    Role = ConversationRole.User,
                    Content = new List<ContentBlock>
                    {
                        new() { Text = userPrompt }
                    }
                }
            },
            InferenceConfig = new InferenceConfiguration
            {
                MaxTokens = _settings.ParsingMaxTokens,
                Temperature = _settings.ParsingTemperature
            }
        };

        var response = await _client.ConverseAsync(request, cancellationToken);
        var outputText = response.Output?.Message?.Content?.FirstOrDefault()?.Text;

        if (string.IsNullOrWhiteSpace(outputText))
        {
            _logger.LogWarning("Bedrock returned empty BOM enrichment response");
            return null;
        }

        _logger.LogDebug("Bedrock BOM enrichment response ({InputTokens}in/{OutputTokens}out): {Response}",
            response.Usage?.InputTokens, response.Usage?.OutputTokens, outputText);

        try
        {
            var json = outputText.Trim();
            if (json.StartsWith("```"))
            {
                var firstNewline = json.IndexOf('\n');
                if (firstNewline > 0)
                    json = json.Substring(firstNewline + 1);
                if (json.EndsWith("```"))
                    json = json.Substring(0, json.Length - 3);
                json = json.Trim();
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new EnrichedEmbeddingText
            {
                Description = root.TryGetProperty("description", out var desc)
                    ? desc.GetString() ?? string.Empty
                    : string.Empty,
                TechnicalSpecs = new List<TechnicalSpecItem>(),
                Enrichment = root.TryGetProperty("enrichment", out var enrich)
                    ? enrich.GetString() ?? string.Empty
                    : string.Empty
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse BOM LLM enrichment JSON: {Response}", outputText);
            return null;
        }
    }

    /// <summary>
    /// Parse a JSON array of spec objects into a list of <see cref="TechnicalSpecItem"/>.
    /// Handles numeric, boolean, and string values.
    /// </summary>
    internal static List<TechnicalSpecItem> ParseTechnicalSpecsArray(JsonElement arrayElement)
    {
        var specs = new List<TechnicalSpecItem>();

        foreach (var item in arrayElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var name = item.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                ? nameProp.GetString()?.Replace('_', ' ')?.Trim() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(name)) continue;

            object? value = null;
            if (item.TryGetProperty("value", out var valueProp))
            {
                value = valueProp.ValueKind switch
                {
                    JsonValueKind.Number => valueProp.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String => valueProp.GetString(),
                    _ => null
                };
            }

            string? uom = null;
            if (item.TryGetProperty("uom", out var uomProp) && uomProp.ValueKind == JsonValueKind.String)
            {
                uom = uomProp.GetString();
            }

            specs.Add(new TechnicalSpecItem { Name = name, Value = value, Uom = uom });
        }

        return specs;
    }

    // ──────────────────────────────────────────────────────
    // Prompt builders
    // ──────────────────────────────────────────────────────

    private static string BuildProductPrompt(ProductEmbeddingInput product)
    {
        var certsText = product.Certifications != null && product.Certifications.Count > 0
            ? string.Join(", ", product.Certifications)
            : "none";

        return EmbeddingTextEnricherPrompts.ProductUserPromptTemplate
            .Replace("{model_name}", product.ModelName ?? "unknown")
            .Replace("{vendor_name}", product.VendorName ?? "unknown")
            .Replace("{family_label}", FormatFamilyLabel(product.FamilyLabel))
            .Replace("{specs}", product.SpecificationsJson ?? "none")
            .Replace("{description}", product.Description ?? "none")
            .Replace("{use_cases}", product.UseCases ?? "none")
            .Replace("{ideal_applications}", product.IdealApplications ?? "none")
            .Replace("{not_recommended_for}", product.NotRecommendedFor ?? "none")
            .Replace("{certifications}", certsText)
            .Replace("{finishes}", product.FinishesJson ?? "none")
            .Replace("{key_features}", product.KeyFeaturesJson ?? "none");
    }

    private static string BuildBomItemPrompt(BomLineItem bomItem, ParsedBomQuery parsedQuery)
    {
        var specsText = bomItem.TechnicalSpecs != null && bomItem.TechnicalSpecs.Count > 0
            ? string.Join(" | ", bomItem.TechnicalSpecs.Select(s =>
                $"{s.Name}: {s.Value} {s.Uom}".Trim()))
            : "none";

        var certsText = bomItem.Certifications != null && bomItem.Certifications.Count > 0
            ? string.Join(", ", bomItem.Certifications)
            : "none";

        var additionalDataText = bomItem.AdditionalData.Count > 0
            ? string.Join(", ", bomItem.AdditionalData
                .Where(kv => kv.Value != null)
                .Select(kv => $"{kv.Key}: {kv.Value}"))
            : "none";

        var attributesText = parsedQuery.Attributes.Count > 0
            ? string.Join(", ", parsedQuery.Attributes.Select(a => $"{a.Key}: {a.Value}"))
            : "none";

        return EmbeddingTextEnricherPrompts.BomItemUserPromptTemplate
            .Replace("{bom_item}", bomItem.BomItem)
            .Replace("{bom_description}", bomItem.Description ?? "none")
            .Replace("{search_query}", parsedQuery.SearchQuery ?? "none")
            .Replace("{category}", bomItem.Category ?? "none")
            .Replace("{technical_specs}", specsText)
            .Replace("{certifications}", certsText)
            .Replace("{notes}", bomItem.Notes ?? "none")
            .Replace("{additional_data}", additionalDataText)
            .Replace("{family}", FormatFamilyLabel(parsedQuery.MaterialFamily))
            .Replace("{attributes}", attributesText);
    }

    // ──────────────────────────────────────────────────────
    // Deterministic fallbacks (used when LLM fails)
    // ──────────────────────────────────────────────────────

    internal static EnrichedEmbeddingText BuildProductFallback(ProductEmbeddingInput product)
    {
        var descParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(product.ModelName))
            descParts.Add(product.ModelName);
        if (!string.IsNullOrWhiteSpace(product.Description))
            descParts.Add(product.Description);

        // Fallback specs: use DimensionUnitConverter deterministic parsing
        var specs = DimensionUnitConverter.ParseJsonToTechnicalSpecItems(product.SpecificationsJson);

        var enrichParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(product.VendorName))
            enrichParts.Add($"Vendor: {product.VendorName}.");
        if (!string.IsNullOrWhiteSpace(product.FamilyLabel))
            enrichParts.Add($"Family: {FormatFamilyLabel(product.FamilyLabel)}.");
        if (!string.IsNullOrWhiteSpace(product.UseCases))
            enrichParts.Add($"Use cases: {product.UseCases}.");
        if (!string.IsNullOrWhiteSpace(product.IdealApplications))
            enrichParts.Add($"Ideal for: {product.IdealApplications}.");
        if (!string.IsNullOrWhiteSpace(product.NotRecommendedFor))
            enrichParts.Add($"Avoid: {product.NotRecommendedFor}.");
        if (!string.IsNullOrWhiteSpace(product.KeyFeaturesJson))
            enrichParts.Add($"Features: {product.KeyFeaturesJson}.");

        return new EnrichedEmbeddingText
        {
            Description = string.Join(" ", descParts),
            TechnicalSpecs = specs,
            Enrichment = string.Join(" ", enrichParts)
        };
    }

    /// <summary>
    /// Deterministic fallback for BOM items when LLM enrichment fails.
    /// Only produces <c>Description</c> and <c>Enrichment</c>.
    /// <c>TechnicalSpecs</c> is always empty — the builder pulls specs from the BOM item directly.
    /// </summary>
    internal static EnrichedEmbeddingText BuildBomItemFallback(BomLineItem bomItem, ParsedBomQuery parsedQuery)
    {
        // Description: merge BOM description with search query synonyms
        var descText = QueryEmbeddingTextBuilder.BuildEnrichedDescription(
            bomItem.Description, parsedQuery.SearchQuery);

        // Enrichment: merge category, family, notes, additional data, attributes
        var enrichParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(bomItem.Category))
            enrichParts.Add($"Category: {bomItem.Category}.");
        if (!string.IsNullOrWhiteSpace(parsedQuery.MaterialFamily))
            enrichParts.Add($"Family: {FormatFamilyLabel(parsedQuery.MaterialFamily)}.");
        if (!string.IsNullOrWhiteSpace(bomItem.Notes))
            enrichParts.Add($"Notes: {bomItem.Notes}.");

        foreach (var kv in bomItem.AdditionalData.Where(kv => kv.Value != null))
        {
            enrichParts.Add($"{kv.Key}: {kv.Value}.");
        }

        foreach (var attr in parsedQuery.Attributes)
        {
            enrichParts.Add($"{attr.Key}: {attr.Value}.");
        }

        return new EnrichedEmbeddingText
        {
            Description = descText,
            TechnicalSpecs = new List<TechnicalSpecItem>(),
            Enrichment = string.Join(" ", enrichParts)
        };
    }

    private static string FormatFamilyLabel(string? familyLabel)
    {
        if (string.IsNullOrWhiteSpace(familyLabel))
            return "unknown";
        var readable = familyLabel.Replace("_", " ");
        return $"{readable} ({familyLabel})";
    }

    public void Dispose()
    {
        if (_ownsClient && _client is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
