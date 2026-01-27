using SourcingEngine.Core.Models;

namespace SourcingEngine.Core.Repositories;

/// <summary>
/// Repository interface for enriched product data from vendor schemas
/// </summary>
public interface IProductEnrichedRepository
{
    /// <summary>
    /// Get enriched data for products from all vendor schemas in parallel
    /// </summary>
    Task<List<ProductEnriched>> GetEnrichedDataAsync(
        IEnumerable<Guid> productIds,
        CancellationToken cancellationToken = default);
}
