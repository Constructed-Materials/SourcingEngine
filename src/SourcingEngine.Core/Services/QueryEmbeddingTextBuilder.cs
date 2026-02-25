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

        // [DESCRIPTION] — the full spec text (closest analog to product description)
        AppendSection(sb, "DESCRIPTION", item.Spec);

        // [USE] — attributes that imply usage context (color, grade, finish, etc.)
        var attributes = BuildAttributesText(parsedQuery);
        AppendSection(sb, "USE", attributes);

        var result = sb.ToString().Trim();

        _logger.LogDebug(
            "Built query embedding text for '{BomItem}': {TextLength} chars",
            item.BomItem, result.Length);

        return result;
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
