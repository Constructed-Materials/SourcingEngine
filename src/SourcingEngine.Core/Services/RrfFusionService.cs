using Microsoft.Extensions.Logging;
using SourcingEngine.Core.Models;
using SourcingEngine.Core.Repositories;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Implements Reciprocal Rank Fusion (RRF) for combining search results.
/// RRF score = Σ (weight / (k + rank)) for each result list
/// </summary>
public class RrfFusionService : ISearchFusionService
{
    private readonly ILogger<RrfFusionService> _logger;

    public RrfFusionService(ILogger<RrfFusionService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public List<MaterialFamily> Fuse(
        IReadOnlyList<RankedMaterialFamily> fullTextResults,
        IReadOnlyList<RankedMaterialFamily> semanticResults,
        float fullTextWeight = 1.0f,
        float semanticWeight = 1.0f,
        int k = 50,
        int maxResults = 10)
    {
        _logger.LogDebug(
            "Fusing results: FTS={FtsCount}, Semantic={SemCount}, Weights=({FtsWeight},{SemWeight}), k={K}",
            fullTextResults.Count, semanticResults.Count, fullTextWeight, semanticWeight, k);

        // Build rank lookup for full-text results (1-indexed ranks)
        var ftsRanks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < fullTextResults.Count; i++)
        {
            ftsRanks[fullTextResults[i].Family.FamilyLabel] = i + 1;
        }

        // Build rank lookup for semantic results (1-indexed ranks)
        var semRanks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < semanticResults.Count; i++)
        {
            semRanks[semanticResults[i].Family.FamilyLabel] = i + 1;
        }

        // Collect all unique families
        var allFamilies = new Dictionary<string, MaterialFamily>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in fullTextResults)
        {
            allFamilies.TryAdd(r.Family.FamilyLabel, r.Family);
        }
        foreach (var r in semanticResults)
        {
            allFamilies.TryAdd(r.Family.FamilyLabel, r.Family);
        }

        // Calculate RRF scores
        var rrfScores = new List<(MaterialFamily Family, float Score)>();
        
        foreach (var kvp in allFamilies)
        {
            var familyLabel = kvp.Key;
            var family = kvp.Value;
            
            float score = 0;
            
            // RRF contribution from full-text results
            if (ftsRanks.TryGetValue(familyLabel, out int ftsRank))
            {
                score += fullTextWeight / (k + ftsRank);
            }
            
            // RRF contribution from semantic results
            if (semRanks.TryGetValue(familyLabel, out int semRank))
            {
                score += semanticWeight / (k + semRank);
            }
            
            rrfScores.Add((family, score));
        }

        // Sort by RRF score descending and take top N
        var result = rrfScores
            .OrderByDescending(x => x.Score)
            .Take(maxResults)
            .Select(x => x.Family)
            .ToList();

        _logger.LogDebug("RRF fusion produced {Count} results", result.Count);
        
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            foreach (var (family, score) in rrfScores.OrderByDescending(x => x.Score).Take(5))
            {
                var ftsRank = ftsRanks.GetValueOrDefault(family.FamilyLabel, -1);
                var semRank = semRanks.GetValueOrDefault(family.FamilyLabel, -1);
                _logger.LogTrace(
                    "  {Label}: RRF={Score:F6} (FTS rank={FtsRank}, Sem rank={SemRank})",
                    family.FamilyLabel, score, ftsRank, semRank);
            }
        }

        return result;
    }
}
