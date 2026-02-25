namespace SourcingEngine.Core.Configuration;

/// <summary>
/// Configuration settings for AWS Bedrock AI services (embeddings and LLM parsing).
/// Used when deploying to the cloud where Ollama is not available.
/// Authentication uses the default AWS credential chain (IAM Role on ECS/Lambda,
/// environment variables, or ~/.aws/credentials profile for local dev).
/// </summary>
public class BedrockSettings
{
    /// <summary>
    /// Whether to enable Bedrock services.
    /// When true, Bedrock implementations are used for IEmbeddingService and IQueryParserService.
    /// Takes priority over Ollama if both are enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// AWS region for Bedrock API calls (default: us-east-1).
    /// Must be a region where your selected models are available.
    /// </summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// Model ID for generating embeddings (default: Amazon Titan Text Embeddings V2).
    /// Supported: amazon.titan-embed-text-v2:0 (256/512/1024 dims)
    /// </summary>
    public string EmbeddingModelId { get; set; } = "amazon.titan-embed-text-v2:0";

    /// <summary>
    /// Dimension of embedding vectors. Must match a dimension supported by the model.
    /// Titan V2 supports: 256, 512, 1024 (default: 1024 for best quality).
    /// </summary>
    public int EmbeddingDimension { get; set; } = 1024;

    /// <summary>
    /// Whether to normalize embedding vectors (default: true).
    /// Normalized vectors enable cosine similarity via dot product.
    /// </summary>
    public bool EmbeddingNormalize { get; set; } = true;

    /// <summary>
    /// Model ID for LLM query parsing. Can be any Bedrock model that supports the Converse API.
    /// Examples:
    ///   - us.amazon.nova-lite-v1:0 (cheap, good at structured JSON — default)
    ///   - us.amazon.nova-micro-v1:0 (cheapest, but weak at complex prompts)
    ///   - us.amazon.nova-pro-v1:0 (most capable, higher cost)
    ///   - us.meta.llama3-1-8b-instruct-v1:0
    /// </summary>
    public string ParsingModelId { get; set; } = "us.amazon.nova-lite-v1:0";

    /// <summary>
    /// Maximum tokens for LLM parsing response.
    /// BOM line parsing typically needs 200-500 tokens.
    /// </summary>
    public int ParsingMaxTokens { get; set; } = 500;

    /// <summary>
    /// Temperature for LLM parsing (0.0-1.0). Low = deterministic.
    /// </summary>
    public float ParsingTemperature { get; set; } = 0.1f;

    /// <summary>
    /// HTTP request timeout in seconds for Bedrock API calls.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum concurrent requests for batch embedding generation.
    /// Prevents Bedrock throttling (default: 5).
    /// </summary>
    public int MaxConcurrentEmbeddings { get; set; } = 5;
}
