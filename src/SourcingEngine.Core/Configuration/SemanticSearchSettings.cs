namespace SourcingEngine.Core.Configuration;

/// <summary>
/// Configuration settings for semantic/vector search functionality.
/// </summary>
public class SemanticSearchSettings
{
    /// <summary>
    /// Whether semantic search is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum number of results to return from vector search.
    /// </summary>
    public int MatchCount { get; set; } = 20;

    /// <summary>
    /// Minimum cosine similarity threshold for product matches (0.0-1.0).
    /// Lower values return more results; typical range 0.25-0.50.
    /// </summary>
    public float SimilarityThreshold { get; set; } = 0.3f;

    /// <summary>
    /// Whether post-retrieval specification re-ranking is enabled.
    /// When true, semantic results are re-scored by blending cosine similarity
    /// with structured specification matching (dimensional proximity + categorical match).
    /// </summary>
    public bool EnableSpecReRanking { get; set; } = true;

    /// <summary>
    /// Weight for the semantic (cosine similarity) component in the re-ranker blended score.
    /// Must sum to 1.0 with <see cref="ReRankerSpecWeight"/>.
    /// </summary>
    public float ReRankerSemanticWeight { get; set; } = 0.6f;

    /// <summary>
    /// Weight for the specification match component in the re-ranker blended score.
    /// Must sum to 1.0 with <see cref="ReRankerSemanticWeight"/>.
    /// </summary>
    public float ReRankerSpecWeight { get; set; } = 0.4f;

    // ── Multi-Vector Embedding Weights ──────────────────

    /// <summary>
    /// Weight for Vector A ([MATERIAL] [PRODUCT] [DESCRIPTION] [CERTIFICATIONS])
    /// in the multi-vector weighted similarity score.
    /// Must sum to 1.0 with <see cref="SpecsVectorWeight"/> and <see cref="EnrichmentVectorWeight"/>.
    /// </summary>
    public float DescriptionVectorWeight { get; set; } = 0.6f;

    /// <summary>
    /// Weight for Vector B ([TECHNICALSPECS]) in the multi-vector weighted similarity score.
    /// </summary>
    public float SpecsVectorWeight { get; set; } = 0.3f;

    /// <summary>
    /// Weight for Vector C ([PRODUCTENRICHMENT]) in the multi-vector weighted similarity score.
    /// </summary>
    public float EnrichmentVectorWeight { get; set; } = 0.1f;

    /// <summary>
    /// How much to widen the description-only retrieval threshold below
    /// <see cref="SimilarityThreshold"/> during two-phase multi-vector search.
    /// The HNSW index retrieves candidates at <c>SimilarityThreshold - RetrievalThresholdMargin</c>
    /// (floored at 0.1) using only the description vector, then the precise
    /// weighted score is computed in C# and filtered against <see cref="SimilarityThreshold"/>.
    /// </summary>
    public float RetrievalThresholdMargin { get; set; } = 0.15f;
}
