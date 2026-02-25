using System.Text;
using Microsoft.Extensions.Logging;
using SourcingEngine.Common.Models;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Builds query text for embedding generation that structurally aligns
/// with the product embedding format (<see cref="ProductEmbeddingTextBuilder"/>).
/// This alignment is critical for cosine similarity to work well.
/// </summary>
public interface IQueryEmbeddingTextBuilder
{
    /// <summary>
    /// Build structured embedding text from a BOM line item and its LLM-parsed data.
    /// The output format mirrors the <c>[SECTION]</c> tags used by <see cref="ProductEmbeddingTextBuilder"/>.
    /// The LLM-generated <see cref="ParsedBomQuery.SearchQuery"/> (synonym-expanded, multi-unit text)
    /// is merged into the <c>[DESCRIPTION]</c> section to boost recall without breaking structural alignment.
    /// </summary>
    string BuildQueryEmbeddingText(BomLineItem item, ParsedBomQuery parsedQuery);
}

/// <inheritdoc />
public class QueryEmbeddingTextBuilder : IQueryEmbeddingTextBuilder
{
    private readonly ILogger<QueryEmbeddingTextBuilder> _logger;

    public QueryEmbeddingTextBuilder(ILogger<QueryEmbeddingTextBuilder> logger)
    {
        _logger = logger;
    }

    public string BuildQueryEmbeddingText(BomLineItem item, ParsedBomQuery parsedQuery)
    {
        var sb = new StringBuilder();

        // [FAMILY] — from LLM-parsed material family
        AppendSection(sb, "FAMILY", FormatFamilyLabel(parsedQuery.MaterialFamily));

        // [TECHNICALSPECS] — dimensions from LLM parsing
        var specs = BuildTechnicalSpecs(parsedQuery);
        AppendSection(sb, "TECHNICALSPECS", specs);

        // [DESCRIPTION] — enriched: raw spec text + LLM synonym/size-expanded search query
        // The SearchQuery from the LLM contains synonyms and unit conversions
        // (e.g., "8 inch 200 mm 20 cm CMU concrete masonry unit concrete block").
        // Merging it into [DESCRIPTION] pushes the query vector closer to products
        // described in any of these variant terms, while keeping [SECTION] alignment intact.
        var descriptionText = BuildEnrichedDescription(item.Spec, parsedQuery.SearchQuery);
        AppendSection(sb, "DESCRIPTION", descriptionText);

        // [USE] — attributes that imply usage context (color, grade, finish, etc.)
        var attributes = BuildAttributesText(parsedQuery);
        AppendSection(sb, "USE", attributes);

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

    private static string BuildTechnicalSpecs(ParsedBomQuery parsedQuery)
    {
        // All measurable specs (dimensions, r-values, u-factors, etc.)
        // now live in TechnicalSpecs.Specs dictionary
        return parsedQuery.TechnicalSpecs.ToEmbeddingFormat();
    }

    private static string BuildAttributesText(ParsedBomQuery parsedQuery)
    {
        var attrs = new List<string>();

        foreach (var attr in parsedQuery.Attributes)
        {
            attrs.Add($"{attr.Key}: {attr.Value}");
        }

        return string.Join(", ", attrs);
    }

    private static string FormatFamilyLabel(string? familyLabel)
    {
        if (string.IsNullOrWhiteSpace(familyLabel))
            return string.Empty;

        // Match ProductEmbeddingTextBuilder format: readable + original
        var readable = familyLabel.Replace("_", " ");
        return $"{readable} ({familyLabel})";
    }

    private static void AppendSection(StringBuilder sb, string sectionName, string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        sb.Append('[').Append(sectionName).Append("] ");
        sb.Append(content.Trim());
        sb.Append(' ');
    }
}
