using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SourcingEngine.Core.Configuration;
using SourcingEngine.Core.Repositories;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Post-retrieval re-ranker that blends semantic similarity scores with
/// structured specification matching. Works across all product families
/// by generically matching dimensional and categorical specs from the
/// parsed query against each product's <c>product_knowledge.specifications</c> JSON.
/// </summary>
public interface ISpecMatchReRanker
{
    /// <summary>
    /// Re-rank semantic search results by blending cosine similarity with
    /// specification proximity scores. Returns a new list sorted by final blended score.
    /// </summary>
    /// <param name="semanticMatches">Raw results from pgvector search</param>
    /// <param name="queryTechnicalSpecs">Parsed specs from LLM query parser</param>
    /// <returns>Re-ranked list with updated similarity scores</returns>
    List<SemanticProductMatch> ReRank(
        List<SemanticProductMatch> semanticMatches,
        TechnicalSpecs? queryTechnicalSpecs);
}

/// <inheritdoc />
public class SpecMatchReRanker : ISpecMatchReRanker
{
    private readonly SemanticSearchSettings _settings;
    private readonly ILogger<SpecMatchReRanker> _logger;

    public SpecMatchReRanker(
        IOptions<SemanticSearchSettings> settings,
        ILogger<SpecMatchReRanker> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public List<SemanticProductMatch> ReRank(
        List<SemanticProductMatch> semanticMatches,
        TechnicalSpecs? queryTechnicalSpecs)
    {
        // If re-ranking is disabled or no query specs to match against, return as-is
        if (!_settings.EnableSpecReRanking ||
            queryTechnicalSpecs == null ||
            queryTechnicalSpecs.Specs.Count == 0 ||
            semanticMatches.Count == 0)
        {
            return semanticMatches;
        }

        var alpha = _settings.SemanticWeight;
        var beta = _settings.SpecMatchWeight;

        _logger.LogDebug(
            "Re-ranking {Count} results with {SpecCount} query specs (α={Alpha}, β={Beta})",
            semanticMatches.Count, queryTechnicalSpecs.Specs.Count, alpha, beta);

        // Normalize query specs to mm for dimensional comparison
        var queryDimensions = NormalizeQuerySpecs(queryTechnicalSpecs);

        var reRanked = new List<(SemanticProductMatch Match, float FinalScore, float SpecScore)>();

        foreach (var match in semanticMatches)
        {
            var specScore = ComputeSpecScore(match, queryDimensions, queryTechnicalSpecs);
            var finalScore = (alpha * match.Similarity) + (beta * specScore);

            reRanked.Add((match, finalScore, specScore));
        }

        // Sort by final blended score descending
        reRanked.Sort((a, b) => b.FinalScore.CompareTo(a.FinalScore));

        _logger.LogDebug("Re-ranking complete. Top result: {Model} (semantic={Semantic:F3}, spec={Spec:F3}, final={Final:F3})",
            reRanked[0].Match.ModelName, reRanked[0].Match.Similarity, reRanked[0].SpecScore, reRanked[0].FinalScore);

        // Return re-ranked matches with FinalScore set
        return reRanked.Select(r => r.Match with { FinalScore = r.FinalScore }).ToList();
    }

    /// <summary>
    /// Normalize query specs to canonical mm values for dimensional comparison.
    /// Non-dimensional specs are kept as-is for exact matching.
    /// </summary>
    private Dictionary<string, DimensionEntry> NormalizeQuerySpecs(TechnicalSpecs querySpecs)
    {
        var normalized = new Dictionary<string, DimensionEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in querySpecs.Specs)
        {
            var parsed = DimensionUnitConverter.ParseDimensionString(value);
            if (parsed != null)
            {
                var mm = DimensionUnitConverter.ToMillimeters(parsed.Value.Value, parsed.Value.Unit);
                normalized[key] = new DimensionEntry(mm, true);
            }
            else
            {
                // Non-dimensional spec (e.g., color, grade) — store raw string
                normalized[key] = new DimensionEntry(0, false, value);
            }
        }

        return normalized;
    }

    /// <summary>
    /// Compute a specification match score between a query and a product.
    /// Dimensional specs use proximity scoring. Non-dimensional use exact match.
    /// Score is 0.0–1.0.
    /// </summary>
    private float ComputeSpecScore(
        SemanticProductMatch match,
        Dictionary<string, DimensionEntry> queryDimensions,
        TechnicalSpecs querySpecs)
    {
        if (string.IsNullOrWhiteSpace(match.SpecificationsJson))
            return 0f;

        Dictionary<string, JsonElement>? productSpecs;
        try
        {
            using var doc = JsonDocument.Parse(match.SpecificationsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return 0f;

            productSpecs = new Dictionary<string, JsonElement>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                productSpecs[prop.Name] = prop.Value.Clone();
            }
        }
        catch (JsonException)
        {
            return 0f;
        }

        var scores = new List<float>();

        foreach (var (queryKey, queryEntry) in queryDimensions)
        {
            if (queryEntry.IsDimensional)
            {
                var dimScore = ComputeDimensionalScore(queryKey, queryEntry.ValueMm, productSpecs);
                if (dimScore.HasValue)
                    scores.Add(dimScore.Value);
            }
            else
            {
                var catScore = ComputeCategoricalScore(queryKey, queryEntry.RawValue, productSpecs);
                if (catScore.HasValue)
                    scores.Add(catScore.Value);
            }
        }

        if (scores.Count == 0)
            return 0f;

        return scores.Average();
    }

    /// <summary>
    /// Score a dimensional query spec against a product's specs.
    /// Uses fuzzy key matching (e.g., query "width" matches product "width_mm", "width_mm_options", "width_inches").
    /// Score: 1 - |query_mm - closest_product_mm| / query_mm, clamped to [0, 1].
    /// </summary>
    private float? ComputeDimensionalScore(
        string queryKey, double queryMm,
        Dictionary<string, JsonElement> productSpecs)
    {
        if (queryMm <= 0)
            return null;

        // Find matching product spec keys — fuzzy match on base key name
        var candidateValues = new List<double>();

        foreach (var (prodKey, prodElement) in productSpecs)
        {
            if (!IsKeyMatch(queryKey, prodKey))
                continue;

            var detected = DimensionUnitConverter.DetectUnit(prodKey);
            var unit = detected?.Unit ?? "mm";

            ExtractNumericValues(prodElement, unit, candidateValues);
        }

        if (candidateValues.Count == 0)
            return null;

        // Find closest value to query
        var closestMm = candidateValues.OrderBy(v => Math.Abs(v - queryMm)).First();
        var proximity = 1.0 - Math.Abs(queryMm - closestMm) / queryMm;
        return (float)Math.Clamp(proximity, 0.0, 1.0);
    }

    /// <summary>
    /// Score a categorical (non-dimensional) query spec against a product's specs.
    /// Exact match → 1.0, contained in array → 1.0, no match → 0.0.
    /// </summary>
    private float? ComputeCategoricalScore(
        string queryKey, string? queryValue,
        Dictionary<string, JsonElement> productSpecs)
    {
        if (string.IsNullOrWhiteSpace(queryValue))
            return null;

        foreach (var (prodKey, prodElement) in productSpecs)
        {
            if (!prodKey.Contains(queryKey, StringComparison.OrdinalIgnoreCase))
                continue;

            if (prodElement.ValueKind == JsonValueKind.String)
            {
                var prodValue = prodElement.GetString();
                if (string.Equals(prodValue, queryValue, StringComparison.OrdinalIgnoreCase))
                    return 1f;
            }
            else if (prodElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in prodElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String &&
                        string.Equals(item.GetString(), queryValue, StringComparison.OrdinalIgnoreCase))
                    {
                        return 1f;
                    }
                }
            }
        }

        return 0f;
    }

    /// <summary>
    /// Check if a query key matches a product spec key.
    /// Handles: exact match, suffix-stripped match, and options-suffixed match.
    /// e.g., query "width" matches "width_mm", "width_mm_options", "available_widths_mm", "width_inches"
    /// </summary>
    internal static bool IsKeyMatch(string queryKey, string productKey)
    {
        var q = queryKey.ToLowerInvariant().Trim();
        var p = productKey.ToLowerInvariant().Trim();

        // Exact match
        if (q == p)
            return true;

        // Strip unit suffix from product key and compare
        var detected = DimensionUnitConverter.DetectUnit(p);
        if (detected != null && detected.Value.BaseKey == q)
            return true;

        // Also handle "options" suffix: "width_mm_options" → base "width"
        var pStripped = p.Replace("_options", "");
        detected = DimensionUnitConverter.DetectUnit(pStripped);
        if (detected != null && detected.Value.BaseKey == q)
            return true;

        // Handle "available_widths_mm" → should match "width"
        // Remove "available_" prefix and singularize
        if (p.StartsWith("available_"))
        {
            var afterPrefix = p["available_".Length..];
            detected = DimensionUnitConverter.DetectUnit(afterPrefix);
            if (detected != null)
            {
                var baseKey = detected.Value.BaseKey;
                // Simple singularize: "widths" → "width"
                if (baseKey.EndsWith("s") && q == baseKey[..^1])
                    return true;
                if (q == baseKey)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Extract numeric values from a JSON element (scalar or array),
    /// converting each to millimeters based on the detected unit.
    /// </summary>
    private static void ExtractNumericValues(
        JsonElement element, string unit, List<double> results)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var val))
        {
            results.Add(DimensionUnitConverter.ToMillimeters(val, unit));
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var arrVal))
                {
                    results.Add(DimensionUnitConverter.ToMillimeters(arrVal, unit));
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            // Try parsing dimensional strings like "8 in"
            var parsed = DimensionUnitConverter.ParseDimensionString(element.GetString());
            if (parsed != null)
            {
                results.Add(DimensionUnitConverter.ToMillimeters(parsed.Value.Value, parsed.Value.Unit));
            }
        }
    }

    private record struct DimensionEntry(double ValueMm, bool IsDimensional, string? RawValue = null);
}
