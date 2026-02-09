using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SourcingEngine.Core.Configuration;
using SourcingEngine.Core.Models;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Hybrid search: runs FamilyFirst and ProductFirst in parallel,
/// then fuses the results using interleaving with deduplication.
/// Semantic matches are preferred (they carry similarity scores).
/// </summary>
public class HybridStrategy : ISearchStrategy
{
    private readonly FamilyFirstStrategy _familyFirst;
    private readonly ProductFirstStrategy _productFirst;
    private readonly SemanticSearchSettings _settings;
    private readonly ILogger<HybridStrategy> _logger;

    public SemanticSearchMode Mode => SemanticSearchMode.Hybrid;

    public HybridStrategy(
        FamilyFirstStrategy familyFirst,
        ProductFirstStrategy productFirst,
        IOptions<SemanticSearchSettings> settings,
        ILogger<HybridStrategy> logger)
    {
        _familyFirst = familyFirst;
        _productFirst = productFirst;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SearchStrategyResult> ExecuteAsync(
        string bomText, BomItem bomItem, CancellationToken cancellationToken)
    {
        // Run both strategies in parallel
        var familyTask = _familyFirst.ExecuteAsync(bomText, bomItem, cancellationToken);
        var productTask = _productFirst.ExecuteAsync(bomText, bomItem, cancellationToken);

        await Task.WhenAll(familyTask, productTask);

        var familyResult = await familyTask;
        var productResult = await productTask;

        // Fuse results
        var fused = FuseProductMatches(
            familyResult.Matches,
            productResult.Matches,
            _settings.MatchCount);

        var warnings = new List<string>();
        warnings.AddRange(familyResult.Warnings);
        warnings.AddRange(productResult.Warnings);

        _logger.LogInformation(
            "Hybrid search: {FamilyCount} family matches, {ProductCount} semantic matches, {FusedCount} fused",
            familyResult.Matches.Count, productResult.Matches.Count, fused.Count);

        return new SearchStrategyResult
        {
            Matches = fused,
            Warnings = warnings,
            FamilyLabel = familyResult.FamilyLabel,
            CsiCode = familyResult.CsiCode
        };
    }

    /// <summary>
    /// Interleave semantic and family results, preferring semantic (higher confidence),
    /// deduplicating by vendor+model.
    /// </summary>
    internal static List<ProductMatch> FuseProductMatches(
        List<ProductMatch> familyMatches,
        List<ProductMatch> semanticMatches,
        int limit)
    {
        var seen = new HashSet<string>();
        var results = new List<ProductMatch>();

        var semanticQueue = new Queue<ProductMatch>(semanticMatches);
        var familyQueue = new Queue<ProductMatch>(familyMatches);

        while (results.Count < limit && (semanticQueue.Count > 0 || familyQueue.Count > 0))
        {
            if (semanticQueue.Count > 0)
            {
                var match = semanticQueue.Dequeue();
                var key = $"{match.Vendor}:{match.ModelName}";
                if (seen.Add(key))
                    results.Add(match);
            }

            if (familyQueue.Count > 0 && results.Count < limit)
            {
                var match = familyQueue.Dequeue();
                var key = $"{match.Vendor}:{match.ModelName}";
                if (seen.Add(key))
                    results.Add(match);
            }
        }

        return results;
    }
}
