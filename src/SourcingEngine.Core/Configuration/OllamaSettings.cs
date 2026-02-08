namespace SourcingEngine.Core.Configuration;

/// <summary>
/// Configuration settings for Ollama AI services (embeddings and LLM)
/// </summary>
public class OllamaSettings
{
    /// <summary>
    /// Base URL for the Ollama API (default: http://localhost:11434)
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>
    /// Model name for generating embeddings (default: nomic-embed-text)
    /// Options: nomic-embed-text (768d), mxbai-embed-large (1024d)
    /// </summary>
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>
    /// Dimension of embedding vectors. Must match the model:
    /// - nomic-embed-text: 768
    /// - mxbai-embed-large: 1024
    /// </summary>
    public int EmbeddingDimension { get; set; } = 768;

    /// <summary>
    /// Model name for query parsing/LLM tasks (default: llama3.2:3b)
    /// </summary>
    public string ParsingModel { get; set; } = "llama3.2:3b";

    /// <summary>
    /// HTTP request timeout in seconds for embedding generation
    /// </summary>
    public int EmbeddingTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// HTTP request timeout in seconds for LLM parsing tasks
    /// </summary>
    public int ParsingTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Whether to enable Ollama services. If false, falls back to LocalEmbeddingService.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
