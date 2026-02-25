namespace SourcingEngine.Data;

/// <summary>
/// Database configuration settings
/// </summary>
public class DatabaseSettings
{
    /// <summary>
    /// PostgreSQL connection string for Supabase
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of concurrent schema queries when enriching products.
    /// Supabase Session Pooler limits connections to pool_size, so firing
    /// 35 parallel queries exhausts the pool. Default 5 keeps it safe.
    /// </summary>
    public int MaxConcurrentSchemaQueries { get; set; } = 5;
}
