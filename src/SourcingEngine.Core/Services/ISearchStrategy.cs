using SourcingEngine.Core.Models;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Result returned by a search strategy.
/// Decouples strategy output from the final <see cref="SearchResult"/> shape.
/// </summary>
public record SearchStrategyResult
{
    /// <summary>Matched products (already enriched with vendor data).</summary>
    public List<ProductMatch> Matches { get; init; } = [];

    /// <summary>Non-fatal warnings generated during execution.</summary>
    public List<string> Warnings { get; init; } = [];

    /// <summary>Resolved family label (may be null if not found).</summary>
    public string? FamilyLabel { get; init; }

    /// <summary>Resolved CSI code (may be null if not found).</summary>
    public string? CsiCode { get; init; }
}

/// <summary>
/// Strategy interface for pluggable search algorithms.
/// Each implementation encapsulates a single search approach
/// (FamilyFirst, ProductFirst, or Hybrid).
/// </summary>
public interface ISearchStrategy
{
    /// <summary>
    /// The <see cref="SemanticSearchMode"/> this strategy handles.
    /// Used by the orchestrator to select the right strategy at runtime.
    /// </summary>
    SemanticSearchMode Mode { get; }

    /// <summary>
    /// Execute the search strategy for the given request.
    /// </summary>
    /// <param name="bomText">Original BOM text (may be used for embedding generation).</param>
    /// <param name="bomItem">Pre-normalized BOM item with keywords, sizes, synonyms.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Strategy-specific search results.</returns>
    Task<SearchStrategyResult> ExecuteAsync(
        string bomText,
        BomItem bomItem,
        CancellationToken cancellationToken);
}
