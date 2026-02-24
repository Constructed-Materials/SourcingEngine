namespace SourcingEngine.BomExtraction.Lambda.Configuration;

/// <summary>
/// Configuration settings specific to the Lambda function.
/// Bound from environment variables / appsettings.json under "Lambda" section.
/// </summary>
public class LambdaSettings
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

    /// <summary>Exchange to publish result messages to.</summary>
    public string ResultExchange { get; set; } = "bom.extraction";

    /// <summary>Routing key for result messages.</summary>
    public string ResultRoutingKey { get; set; } = "extract.result";

    /// <summary>Lambda /tmp directory for downloaded BOM files.</summary>
    public string TempDirectory { get; set; } = "/tmp/bom-extraction";
}
