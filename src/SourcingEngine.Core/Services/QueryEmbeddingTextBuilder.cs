using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SourcingEngine.Common.Models;
using SourcingEngine.Core.Models;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Builds query text for embedding generation that structurally aligns
/// with the product embedding format (<see cref="ProductEmbeddingTextBuilder"/>).
/// Uses the unified 6-section format:
/// [MATERIAL] [PRODUCT] [DESCRIPTION] [TECHNICALSPECS] [CERTIFICATIONS] [PRODUCTENRICHMENT]
/// </summary>
public interface IQueryEmbeddingTextBuilder
{
    /// <summary>
    /// Build structured embedding text from a BOM line item and its LLM-parsed data.
    /// The output format mirrors the <c>[SECTION]</c> tags used by <see cref="ProductEmbeddingTextBuilder"/>.
    /// All 6 section labels are always present, even when empty (using "[]" placeholder).
    /// </summary>
    Task<string> BuildQueryEmbeddingTextAsync(
        BomLineItem item, ParsedBomQuery parsedQuery, CancellationToken cancellationToken = default);

    /// <summary>
    /// Build three separate embedding texts for multi-vector BOM query embeddings.
    /// Vector A: [MATERIAL] [PRODUCT] [DESCRIPTION] [CERTIFICATIONS]
    /// Vector B: [TECHNICALSPECS]
    /// Vector C: [PRODUCTENRICHMENT]
    /// </summary>
    Task<MultiVectorEmbeddingText> BuildMultiVectorQueryTextAsync(
        BomLineItem item, ParsedBomQuery parsedQuery, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public class QueryEmbeddingTextBuilder : IQueryEmbeddingTextBuilder
{
    private readonly IEmbeddingTextEnricher _enricher;
    private readonly ILogger<QueryEmbeddingTextBuilder> _logger;

    public QueryEmbeddingTextBuilder(
        IEmbeddingTextEnricher enricher,
        ILogger<QueryEmbeddingTextBuilder> logger)
    {
        _enricher = enricher;
        _logger = logger;
    }

    public async Task<string> BuildQueryEmbeddingTextAsync(
        BomLineItem item, ParsedBomQuery parsedQuery, CancellationToken cancellationToken = default)
    {
        // Get LLM-enriched description and enrichment text
        var enriched = await _enricher.EnrichBomItemTextAsync(item, parsedQuery, cancellationToken);

        var sb = new StringBuilder();

        // [MATERIAL] — primary construction material
        AppendSection(sb, "MATERIAL", ResolveMaterial(item, parsedQuery));

        // [PRODUCT] — BOM item name (always present)
        AppendSection(sb, "PRODUCT", item.BomItem);

        // [DESCRIPTION] — LLM-generated fluent description (includes synonym expansion)
        AppendSection(sb, "DESCRIPTION", enriched.Description);

        // [TECHNICALSPECS] — directly from BOM item (not LLM), JSON-serialized
        var specsJson = item.TechnicalSpecs != null && item.TechnicalSpecs.Count > 0
            ? JsonSerializer.Serialize(item.TechnicalSpecs)
            : null;
        AppendSection(sb, "TECHNICALSPECS", specsJson);

        // [CERTIFICATIONS] — from BOM item certifications
        var certsText = item.Certifications != null && item.Certifications.Count > 0
            ? string.Join(", ", item.Certifications)
            : null;
        AppendSection(sb, "CERTIFICATIONS", certsText);

        // [PRODUCTENRICHMENT] — LLM-generated: merged additional data, notes, attributes, family
        AppendSection(sb, "PRODUCTENRICHMENT", enriched.Enrichment);

        var result = sb.ToString().Trim();

        _logger.LogDebug(
            "Built query embedding text for '{BomItem}': {TextLength} chars",
            item.BomItem, result.Length);

        return result;
    }

    /// <inheritdoc />
    public async Task<MultiVectorEmbeddingText> BuildMultiVectorQueryTextAsync(
        BomLineItem item, ParsedBomQuery parsedQuery, CancellationToken cancellationToken = default)
    {
        var enriched = await _enricher.EnrichBomItemTextAsync(item, parsedQuery, cancellationToken);

        var materialText = ResolveMaterial(item, parsedQuery);

        // Vector A: [MATERIAL] [PRODUCT] [DESCRIPTION] [CERTIFICATIONS]
        var descSb = new StringBuilder();
        AppendSection(descSb, "MATERIAL", materialText);
        AppendSection(descSb, "PRODUCT", item.BomItem);
        AppendSection(descSb, "DESCRIPTION", enriched.Description);
        var certsText = item.Certifications != null && item.Certifications.Count > 0
            ? string.Join(", ", item.Certifications)
            : null;
        AppendSection(descSb, "CERTIFICATIONS", certsText);

        // Vector B: [TECHNICALSPECS]
        var specsSb = new StringBuilder();
        var specsJson = item.TechnicalSpecs != null && item.TechnicalSpecs.Count > 0
            ? JsonSerializer.Serialize(item.TechnicalSpecs)
            : null;
        AppendSection(specsSb, "TECHNICALSPECS", specsJson);

        // Vector C: [PRODUCTENRICHMENT]
        var enrichSb = new StringBuilder();
        AppendSection(enrichSb, "PRODUCTENRICHMENT", enriched.Enrichment);

        var descriptionText = descSb.ToString().Trim();
        var specsText = specsSb.ToString().Trim();
        var enrichmentText = enrichSb.ToString().Trim();
        var fullDebug = $"{descriptionText} {specsText} {enrichmentText}";

        _logger.LogDebug(
            "Built multi-vector query text for '{BomItem}': desc={DescLen}, specs={SpecsLen}, enrich={EnrichLen}",
            item.BomItem, descriptionText.Length, specsText.Length, enrichmentText.Length);

        return new MultiVectorEmbeddingText
        {
            DescriptionText = descriptionText,
            SpecsText = specsText,
            EnrichmentText = enrichmentText,
            FullDebugText = fullDebug
        };
    }

    /// <summary>
    /// Resolve the material for a BOM item. Uses <see cref="BomLineItem.Material"/>
    /// when available, otherwise falls back to <c>parsedQuery.Attributes["material"]</c>.
    /// </summary>
    internal static string? ResolveMaterial(BomLineItem item, ParsedBomQuery parsedQuery)
    {
        if (!string.IsNullOrWhiteSpace(item.Material))
            return item.Material;

        if (parsedQuery.Attributes.TryGetValue("material", out var attrMaterial) &&
            !string.IsNullOrWhiteSpace(attrMaterial))
            return attrMaterial;

        return null;
    }

    /// <summary>
    /// Merge the raw spec text with the LLM-expanded search query, deduplicating
    /// overlapping tokens so the embedding isn't inflated with repeated terms.
    /// </summary>
    internal static string BuildEnrichedDescription(string? spec, string? searchQuery)
    {
        var specText = spec?.Trim() ?? string.Empty;
        var queryText = searchQuery?.Trim() ?? string.Empty;

        // If no search query or it's identical to the spec, just return spec
        if (string.IsNullOrWhiteSpace(queryText) ||
            string.Equals(specText, queryText, StringComparison.OrdinalIgnoreCase))
        {
            return specText;
        }

        // Split both into tokens, deduplicate (case-insensitive), preserve order
        var specTokens = specText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var seen = new HashSet<string>(specTokens, StringComparer.OrdinalIgnoreCase);

        var additional = new List<string>();
        foreach (var token in queryText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (seen.Add(token))
            {
                additional.Add(token);
            }
        }

        if (additional.Count == 0)
            return specText;

        return $"{specText} {string.Join(' ', additional)}";
    }

    /// <summary>
    /// Append a section to the embedding text. Always emits the label,
    /// using "[]" as placeholder when content is empty, to maintain
    /// structural alignment between product and query embeddings.
    /// </summary>
    private static void AppendSection(StringBuilder sb, string sectionName, string? content)
    {
        sb.Append('[').Append(sectionName).Append("] ");
        if (string.IsNullOrWhiteSpace(content))
            sb.Append("[]");
        else
            sb.Append(content.Trim());
        sb.Append(' ');
    }
}
