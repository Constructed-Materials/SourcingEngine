using SourcingEngine.Core.Models;

namespace SourcingEngine.Core.Repositories;

/// <summary>
/// Repository interface for semantic vector search on products
/// </summary>
public interface ISemanticProductRepository
{
    /// <summary>
    /// Find products by semantic similarity to query embedding
    /// </summary>
    /// <param name="queryEmbedding">768-dimensional embedding vector from query text</param>
    /// <param name="matchThreshold">Minimum cosine similarity threshold (0.0-1.0)</param>
    /// <param name="matchCount">Maximum number of results to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of products with similarity scores, ordered by similarity descending</returns>
    Task<List<SemanticProductMatch>> SearchByEmbeddingAsync(
        float[] queryEmbedding,
        float matchThreshold = 0.3f,
        int matchCount = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Find products by semantic similarity, filtered by family label
    /// </summary>
    Task<List<SemanticProductMatch>> SearchByEmbeddingAsync(
        float[] queryEmbedding,
        string? familyLabel,
        float matchThreshold = 0.3f,
        int matchCount = 20,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Product match result from semantic search
/// </summary>
public record SemanticProductMatch
{
    public Guid ProductId { get; init; }
    public required string VendorName { get; init; }
    public required string ModelName { get; init; }
    public string? FamilyLabel { get; init; }
    public string? CsiCode { get; init; }
    public string? Description { get; init; }
    public string? UseCases { get; init; }
    public string? SpecificationsJson { get; init; }
    public string? EmbeddingText { get; init; }
    
    /// <summary>
    /// Cosine similarity score (0.0 to 1.0, higher is more similar)
    /// </summary>
    public float Similarity { get; init; }
}
