using SourcingEngine.Core.Models;

namespace SourcingEngine.Core.Services;

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
}
