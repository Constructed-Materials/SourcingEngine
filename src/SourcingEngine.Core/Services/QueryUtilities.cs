using System.Text.Json;
using System.Text.RegularExpressions;
using SourcingEngine.Core.Models;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Pure helper methods shared across search strategies.
/// Static and side-effect-free—easy to unit-test in isolation.
/// </summary>
public static class QueryUtilities
{
    /// <summary>
    /// Cleans a query string for PostgreSQL full-text search by removing
    /// size/dimension patterns that break websearch_to_tsquery.
    /// </summary>
    public static string CleanQueryForFts(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return query;

        // Remove size patterns: 8", 8 inch, 8-inch, 20cm, 200mm, etc.
        var cleaned = Regex.Replace(
            query,
            @"\b\d+(\.\d+)?\s*(""|''|inch|in|inches|cm|mm|feet|ft|foot|meter|m|metres|meters)\b",
            " ",
            RegexOptions.IgnoreCase);

        // Handle patterns like 8x8, 8x8x16, 4'x8'
        cleaned = Regex.Replace(
            cleaned,
            @"\b\d+(\.\d+)?[''′]?\s*[xX×]\s*\d+(\.\d+)?[''′]?(\s*[xX×]\s*\d+(\.\d+)?[''′]?)?\b",
            " ",
            RegexOptions.IgnoreCase);

        // Remove fractions: 5/8, 1/2, 3/4, 5/8", etc.
        cleaned = Regex.Replace(
            cleaned,
            @"\b\d+/\d+\s*(""|''|inch|in|inches|cm|mm)?\b",
            " ",
            RegexOptions.IgnoreCase);

        // Remove standalone quotes that might remain
        cleaned = Regex.Replace(cleaned, @"[""]+", " ");

        // Remove standalone numbers (not useful for FTS, break queries)
        cleaned = Regex.Replace(cleaned, @"\b\d+(\.\d+)?\b", " ");

        // Remove context/preposition words that cause AND failures
        var contextWords = new[] { "on", "for", "with", "over", "under", "around", "between", "above", "below" };
        foreach (var word in contextWords)
        {
            cleaned = Regex.Replace(cleaned, $@"\b{word}\b", " ", RegexOptions.IgnoreCase);
        }

        // Normalize whitespace
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        return cleaned;
    }

    /// <summary>
    /// Safely parse a JSON array string into a list of strings.
    /// Returns null on invalid/empty input.
    /// </summary>
    public static List<string>? ParseJsonArray(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Safely parse a JSON object string into a dictionary.
    /// Returns null on invalid/empty input.
    /// </summary>
    public static Dictionary<string, object>? ParseJsonObject(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Build a <see cref="ProductMatch"/> from a base <see cref="Product"/>.
    /// Enrichment data (description, use_cases, specifications) comes from public.product_knowledge
    /// via the semantic search results, not from vendor schemas.
    /// </summary>
    public static ProductMatch CreateProductMatch(Product product)
    {
        return new ProductMatch
        {
            ProductId = product.ProductId,
            Vendor = product.VendorName,
            ModelName = product.ModelName,
            CsiCode = product.CsiSectionCode
        };
    }
}
