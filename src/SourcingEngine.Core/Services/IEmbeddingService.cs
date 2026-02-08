namespace SourcingEngine.Core.Services;

/// <summary>
/// Service for generating text embeddings for semantic search
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Generate an embedding vector for the given text
    /// </summary>
    /// <param name="text">The text to embed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Float array representing the embedding vector. Dimension depends on provider: 384 (local bge-micro-v2) or 768 (Ollama nomic-embed-text)</returns>
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate embeddings for multiple texts (batch operation)
    /// </summary>
    /// <param name="texts">Collection of texts to embed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of embedding vectors in the same order as input texts</returns>
    Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The dimension of the embedding vectors produced by this service
    /// </summary>
    int EmbeddingDimension { get; }
}
