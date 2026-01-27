using Microsoft.Extensions.Options;
using Npgsql;

namespace SourcingEngine.Data;

/// <summary>
/// Factory for creating Npgsql database connections
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>
    /// Create a new open database connection
    /// </summary>
    Task<NpgsqlConnection> CreateConnectionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Npgsql connection factory implementation
/// </summary>
public class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public NpgsqlConnectionFactory(IOptions<DatabaseSettings> settings)
    {
        _connectionString = settings.Value.ConnectionString;
    }

    public async Task<NpgsqlConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
