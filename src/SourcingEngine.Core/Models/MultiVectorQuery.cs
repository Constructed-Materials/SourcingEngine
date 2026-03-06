namespace SourcingEngine.Core.Models;

/// <summary>
/// Holds the three embedding vectors generated from a BOM query item,
/// one for each vector group. Passed to the repository for two-phase
/// multi-vector semantic search.
/// </summary>
public record MultiVectorQuery
{
    /// <summary>
    /// Embedding for Vector A: [MATERIAL] [PRODUCT] [DESCRIPTION] [CERTIFICATIONS].
    /// Used for HNSW-accelerated initial retrieval (highest weight).
    /// </summary>
    public required float[] DescriptionEmbedding { get; init; }

    /// <summary>
    /// Embedding for Vector B: [TECHNICALSPECS].
    /// Used for specification-level matching in the re-scoring phase.
    /// </summary>
    public required float[] SpecsEmbedding { get; init; }

    /// <summary>
    /// Embedding for Vector C: [PRODUCTENRICHMENT].
    /// Used for contextual/vendor matching in the re-scoring phase.
    /// </summary>
    public required float[] EnrichmentEmbedding { get; init; }
}
