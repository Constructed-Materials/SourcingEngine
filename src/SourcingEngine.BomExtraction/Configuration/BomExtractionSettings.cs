namespace SourcingEngine.BomExtraction.Configuration;

/// <summary>
/// Configuration settings for BOM extraction via AWS Bedrock.
/// Bound from the "BomExtraction" section in appsettings.json.
/// Authentication uses the default AWS credential chain
/// (IAM Role on ECS/Lambda, environment variables, or ~/.aws/credentials profile).
/// </summary>
public class BomExtractionSettings
{
    public const string SectionName = "BomExtraction";

    /// <summary>
    /// AWS region for Bedrock API calls.
    /// Must be a region where the selected model is available.
    /// </summary>
    public string Region { get; set; } = "us-east-2";

    /// <summary>
    /// Bedrock model ID or inference profile ID for BOM extraction.
    /// Default: Amazon Nova Pro cross-region inference profile — native document support, 300K context window.
    /// </summary>
    public string ModelId { get; set; } = "us.amazon.nova-pro-v1:0";

    /// <summary>
    /// Maximum tokens for the model response.
    /// BOM extraction with many line items needs a generous budget.
    /// </summary>
    public int MaxTokens { get; set; } = 5000;

    /// <summary>
    /// Temperature for generation (0.0–1.0). Low = deterministic.
    /// BOM extraction should be deterministic.
    /// </summary>
    public float Temperature { get; set; } = 0.0f;

    /// <summary>
    /// HTTP request timeout in seconds for Bedrock API calls.
    /// Document processing can take longer than simple text queries.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;
}
