using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SourcingEngine.Common.Models;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Builds query text for embedding generation that structurally aligns
/// with the product embedding format (<see cref="ProductEmbeddingTextBuilder"/>).
/// Uses the unified 5-section format:
/// [PRODUCT] [DESCRIPTION] [TECHNICALSPECS] [CERTIFICATIONS] [PRODUCTENRICHMENT]
/// </summary>
public interface IQueryEmbeddingTextBuilder
{
    /// <summary>
    /// Build structured embedding text from a BOM line item and its LLM-parsed data.
    /// The output format mirrors the <c>[SECTION]</c> tags used by <see cref="ProductEmbeddingTextBuilder"/>.
    /// All 5 section labels are always present, even when empty (using "[]" placeholder).
    /// </summary>
    Task<string> BuildQueryEmbeddingTextAsync(
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

        // [PRODUCT] — BOM item name (always present)
        AppendSection(sb, "PRODUCT", item.BomItem);

        // [DESCRIPTION] — LLM-generated fluent description (includes synonym expansion)
        AppendSection(sb, "DESCRIPTION", enriched.Description);

        // [TECHNICALSPECS] — JSON array of {name, value, uom} spec objects
        var specsJson = enriched.TechnicalSpecs.Count > 0
            ? JsonSerializer.Serialize(enriched.TechnicalSpecs)
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
