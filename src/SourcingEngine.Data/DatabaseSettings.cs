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
}
