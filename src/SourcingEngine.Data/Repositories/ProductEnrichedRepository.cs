using Microsoft.Extensions.Logging;
using SourcingEngine.Core.Models;
using SourcingEngine.Core.Repositories;

namespace SourcingEngine.Data.Repositories;

/// <summary>
/// Product enriched repository with parallel schema querying
/// </summary>
public class ProductEnrichedRepository : IProductEnrichedRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ISchemaDiscoveryService _schemaDiscovery;
    private readonly ILogger<ProductEnrichedRepository> _logger;

    public ProductEnrichedRepository(
        IDbConnectionFactory connectionFactory,
        ISchemaDiscoveryService schemaDiscovery,
        ILogger<ProductEnrichedRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _schemaDiscovery = schemaDiscovery;
        _logger = logger;
    }

    public async Task<List<ProductEnriched>> GetEnrichedDataAsync(
        IEnumerable<Guid> productIds,
        CancellationToken cancellationToken = default)
    {
        var productIdList = productIds.ToList();
        if (productIdList.Count == 0)
        {
            return [];
        }

        var schemas = await _schemaDiscovery.GetVendorSchemasAsync(cancellationToken);
        
        _logger.LogInformation("Querying {SchemaCount} vendor schemas in parallel for {ProductCount} products",
            schemas.Count, productIdList.Count);

        // Query all schemas in parallel
        var tasks = schemas.Select(schema => 
            QuerySchemaAsync(schema, productIdList, cancellationToken));

        var results = await Task.WhenAll(tasks);

        // Flatten results
        var enrichedProducts = results
            .SelectMany(r => r)
            .ToList();

        _logger.LogInformation("Found {Count} enriched product records across all schemas", 
            enrichedProducts.Count);

        return enrichedProducts;
    }

    /// <summary>
    /// Regex to validate schema names — only lowercase letters, digits, and underscores allowed.
    /// Prevents SQL injection via schema name interpolation (schema names cannot be parameterized in PostgreSQL).
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex SafeSchemaNamePattern = 
        new(@"^[a-z_][a-z0-9_]*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private async Task<List<ProductEnriched>> QuerySchemaAsync(
        string schemaName,
        List<Guid> productIds,
        CancellationToken cancellationToken)
    {
        var results = new List<ProductEnriched>();

        // Validate schema name to prevent SQL injection (schema names can't be parameterized)
        if (!SafeSchemaNamePattern.IsMatch(schemaName))
        {
            _logger.LogWarning("Rejected unsafe schema name: {SchemaName}", schemaName);
            return results;
        }

        try
        {
            // Build IN clause for product IDs
            var idParams = productIds.Select((_, i) => $"@id{i}").ToList();
            
            // Query only guaranteed columns across all vendor schemas
            var sql = $@"
                SELECT product_id, model_code, use_when, 
                       key_features::text, technical_specs::text, performance_data::text
                FROM {schemaName}.products_enriched
                WHERE product_id IN ({string.Join(", ", idParams)})";

            await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            for (int i = 0; i < productIds.Count; i++)
            {
                var param = command.CreateParameter();
                param.ParameterName = $"id{i}";
                param.Value = productIds[i];
                command.Parameters.Add(param);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new ProductEnriched
                {
                    ProductId = reader.GetGuid(0),
                    ModelCode = reader.IsDBNull(1) ? null : reader.GetString(1),
                    UseWhen = reader.IsDBNull(2) ? null : reader.GetString(2),
                    KeyFeaturesJson = reader.IsDBNull(3) ? null : reader.GetString(3),
                    TechnicalSpecsJson = reader.IsDBNull(4) ? null : reader.GetString(4),
                    PerformanceDataJson = reader.IsDBNull(5) ? null : reader.GetString(5),
                    SourceSchema = schemaName
                });
            }

            if (results.Count > 0)
            {
                _logger.LogDebug("Found {Count} enriched records in schema {Schema}", 
                    results.Count, schemaName);
            }
        }
        catch (Exception ex)
        {
            // Log warning but don't throw - return partial results
            _logger.LogWarning(ex, "Failed to query schema {Schema} - continuing with partial results", 
                schemaName);
        }

        return results;
    }
}
