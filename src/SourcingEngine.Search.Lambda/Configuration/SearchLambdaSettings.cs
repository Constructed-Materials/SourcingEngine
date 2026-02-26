namespace SourcingEngine.Search.Lambda.Configuration;

/// <summary>
/// Configuration settings specific to the SourcingEngine Search Lambda function.
/// Bound from environment variables / appsettings.json under "Lambda" section.
/// </summary>
public class SearchLambdaSettings
{
    public const string SectionName = "Lambda";

    /// <summary>Amazon MQ broker hostname (e.g. b-xxxx.mq.us-east-2.on.aws).</summary>
    public string BrokerHost { get; set; } = string.Empty;

    /// <summary>AMQPS port (default 5671).</summary>
    public int BrokerPort { get; set; } = 5671;

    /// <summary>
    /// ARN of the Secrets Manager secret holding broker credentials.
    /// Expected JSON: {"username":"...","password":"..."}
    /// </summary>
    public string BrokerSecretArn { get; set; } = string.Empty;

    /// <summary>
    /// Direct broker username — used for local development when no secret ARN is set.
    /// </summary>
    public string BrokerUsername { get; set; } = string.Empty;

    /// <summary>
    /// Direct broker password — used for local development when no secret ARN is set.
    /// </summary>
    public string BrokerPassword { get; set; } = string.Empty;

    /// <summary>Whether to use SSL for the broker connection (default true for Amazon MQ).</summary>
    public bool BrokerUseSsl { get; set; } = true;

    /// <summary>
    /// ARN of the Secrets Manager secret holding the database connection string.
    /// Expected JSON: {"connectionString":"Host=...;Password=...;..."}
    /// When set, overrides Database:ConnectionString at Lambda cold start.
    /// </summary>
    public string DatabaseSecretArn { get; set; } = string.Empty;

    /// <summary>Exchange to publish search result messages to.</summary>
    public string ResultExchange { get; set; } = "sourcing.engine";

    /// <summary>Routing key for search result messages (items with matches).</summary>
    public string ResultRoutingKey { get; set; } = "search.result";

    /// <summary>Routing key for zero-result messages (items with no matches).</summary>
    public string ZeroResultRoutingKey { get; set; } = "search.zero-result";
}
