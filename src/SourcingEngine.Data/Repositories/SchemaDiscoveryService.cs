using Microsoft.Extensions.Logging;

namespace SourcingEngine.Data.Repositories;

/// <summary>
/// Schema discovery service implementation
/// </summary>
public class SchemaDiscoveryService : ISchemaDiscoveryService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<SchemaDiscoveryService> _logger;
    private IReadOnlyList<string>? _cachedSchemas;

    public SchemaDiscoveryService(
        IDbConnectionFactory connectionFactory,
        ILogger<SchemaDiscoveryService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> GetVendorSchemasAsync(CancellationToken cancellationToken = default)
    {
        // Return cached result if available
        if (_cachedSchemas != null)
        {
            return _cachedSchemas;
        }

        const string sql = @"
            SELECT table_schema 
            FROM information_schema.tables 
            WHERE table_name = 'products_enriched' 
            AND table_schema NOT IN ('public', 'information_schema', 'pg_catalog')
            ORDER BY table_schema";

        var schemas = new List<string>();

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                schemas.Add(reader.GetString(0));
            }

            _cachedSchemas = schemas.AsReadOnly();
            _logger.LogInformation("Discovered {Count} vendor schemas: {Schemas}", 
                schemas.Count, string.Join(", ", schemas));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover vendor schemas");
            throw;
        }

        return _cachedSchemas;
    }
}
