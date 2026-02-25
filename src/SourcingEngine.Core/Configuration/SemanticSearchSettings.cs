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
}
