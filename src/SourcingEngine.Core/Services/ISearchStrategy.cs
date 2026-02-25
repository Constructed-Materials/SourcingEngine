using SourcingEngine.Common.Models;
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
/// Accepts a <see cref="BomLineItem"/> from the extraction pipeline.
/// </summary>
public interface ISearchStrategy
{
    /// <summary>
    /// Execute the search strategy for the given BOM line item.
    /// </summary>
    /// <param name="item">BOM line item from the extraction pipeline.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Strategy-specific search results.</returns>
    Task<SearchStrategyResult> ExecuteAsync(
        BomLineItem item,
        CancellationToken cancellationToken);
}
