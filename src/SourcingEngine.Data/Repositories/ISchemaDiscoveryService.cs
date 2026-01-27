namespace SourcingEngine.Data.Repositories;

/// <summary>
/// Service for dynamically discovering vendor schemas
/// </summary>
public interface ISchemaDiscoveryService
{
    /// <summary>
    /// Get all vendor schemas that have a products_enriched table
    /// </summary>
    Task<IReadOnlyList<string>> GetVendorSchemasAsync(CancellationToken cancellationToken = default);
}
