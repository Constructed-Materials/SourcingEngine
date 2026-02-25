using SourcingEngine.Core.Models;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Main search orchestrator that processes BOM extraction results
/// and matches each line item to products.
/// </summary>
public interface ISearchOrchestrator
{
    /// <summary>
    /// Execute full search pipeline for all BOM items in a sourcing request.
    /// </summary>
    /// <param name="request">Sourcing request containing extraction results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregate result with per-item product matches.</returns>
    Task<SourcingResult> SearchAsync(SourcingRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Convenience overload: search for a single BOM spec text.
    /// Internally wraps in a 1-item <see cref="SourcingRequest"/>.
    /// </summary>
    /// <param name="bomText">Raw BOM line item text (used as the spec).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search result for the single item.</returns>
    Task<SearchResult> SearchAsync(string bomText, CancellationToken cancellationToken = default);
}
