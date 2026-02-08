using SourcingEngine.Core.Models;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Search mode for semantic vs keyword search
/// </summary>
public enum SemanticSearchMode
{
    /// <summary>
    /// Semantic search disabled, use only keyword/family-based search
    /// </summary>
    Off,

    /// <summary>
    /// Family-first: Find family via semantic search, then search products by family
    /// (original hybrid approach)
    /// </summary>
    FamilyFirst,

    /// <summary>
    /// Product-first: Search products directly by semantic similarity
    /// (bypasses family resolution, uses product embeddings)
    /// </summary>
    ProductFirst,

    /// <summary>
    /// Hybrid: Run both FamilyFirst and ProductFirst in parallel and fuse results
    /// </summary>
    Hybrid
}

/// <summary>
/// Main search orchestrator that chains all search steps
/// </summary>
public interface ISearchOrchestrator
{
    /// <summary>
    /// Execute full search pipeline for a BOM item
    /// </summary>
    /// <param name="bomText">Raw BOM line item text</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Complete search result with matched products</returns>
    Task<SearchResult> SearchAsync(string bomText, CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute search with explicit semantic search mode
    /// </summary>
    /// <param name="bomText">Raw BOM line item text</param>
    /// <param name="mode">Semantic search mode to use</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Complete search result with matched products</returns>
    Task<SearchResult> SearchAsync(string bomText, SemanticSearchMode mode, CancellationToken cancellationToken = default);
}
