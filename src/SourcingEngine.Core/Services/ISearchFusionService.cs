using SourcingEngine.Core.Models;
using SourcingEngine.Core.Repositories;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Service for fusing multiple ranked search result lists
/// </summary>
public interface ISearchFusionService
{
    /// <summary>
    /// Fuse full-text and semantic search results using Reciprocal Rank Fusion (RRF)
    /// </summary>
    /// <param name="fullTextResults">Ranked results from full-text search</param>
    /// <param name="semanticResults">Ranked results from semantic/vector search</param>
    /// <param name="fullTextWeight">Weight for full-text results (default 1.0)</param>
    /// <param name="semanticWeight">Weight for semantic results (default 1.0)</param>
    /// <param name="k">RRF smoothing constant (default 50)</param>
    /// <param name="maxResults">Maximum number of fused results to return</param>
    /// <returns>Fused and re-ranked list of material families</returns>
    List<MaterialFamily> Fuse(
        IReadOnlyList<RankedMaterialFamily> fullTextResults,
        IReadOnlyList<RankedMaterialFamily> semanticResults,
        float fullTextWeight = 1.0f,
        float semanticWeight = 1.0f,
        int k = 50,
        int maxResults = 10);
}
