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

    /// <summary>
    /// Find products by semantic similarity with structured inline filtering (hybrid search).
    /// Filters are applied as WHERE conditions during the vector search, not post-search.
    /// This is the preferred overload for production use — combines vector ranking with
    /// structured metadata filters in a single SQL query for optimal performance.
    /// </summary>
    Task<List<SemanticProductMatch>> SearchByEmbeddingAsync(
        float[] queryEmbedding,
        SearchFilters? filters,
        float matchThreshold = 0.3f,
        int matchCount = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Two-phase multi-vector semantic search.
    /// Phase 1: Retrieve candidates using the description embedding (HNSW-accelerated)
    /// with a widened threshold.
    /// Phase 2: Re-score each candidate using a weighted combination of 3 cosine similarities
    /// (description, specs, enrichment) and filter against the actual threshold.
    /// </summary>
    /// <param name="query">Three embedding vectors for the BOM query</param>
    /// <param name="filters">Optional structured search filters</param>
    /// <param name="matchThreshold">Final similarity threshold after weighted scoring</param>
    /// <param name="retrievalThreshold">Widened threshold for phase-1 description-only retrieval</param>
    /// <param name="matchCount">Maximum results to return</param>
    /// <param name="weights">Weight triple (description, specs, enrichment) — must sum to 1.0</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<List<SemanticProductMatch>> SearchByMultiVectorAsync(
        MultiVectorQuery query,
        SearchFilters? filters,
        float matchThreshold,
        float retrievalThreshold,
        int matchCount,
        (float Description, float Specs, float Enrichment) weights,
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

    /// <summary>
    /// Blended score after spec re-ranking (semantic × α + spec × β).
    /// Null if re-ranking was not applied. When set, results should be
    /// sorted by this score rather than <see cref="Similarity"/>.
    /// </summary>
    public float? FinalScore { get; init; }

    /// <summary>
    /// Cosine similarity for Vector A ([MATERIAL] [PRODUCT] [DESCRIPTION] [CERTIFICATIONS]).
    /// Populated only by multi-vector search.
    /// </summary>
    public float? DescriptionSimilarity { get; init; }

    /// <summary>
    /// Cosine similarity for Vector B ([TECHNICALSPECS]).
    /// Populated only by multi-vector search.
    /// </summary>
    public float? SpecsSimilarity { get; init; }

    /// <summary>
    /// Cosine similarity for Vector C ([PRODUCTENRICHMENT]).
    /// Populated only by multi-vector search.
    /// </summary>
    public float? EnrichmentSimilarity { get; init; }
}
