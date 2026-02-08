using SourcingEngine.Core.Services;

namespace SourcingEngine.Core.Configuration;

/// <summary>
/// Configuration settings for semantic search functionality
/// </summary>
public class SemanticSearchSettings
{
    /// <summary>
    /// Whether semantic search is enabled. If false, falls back to keyword search.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Default semantic search mode when not explicitly specified.
    /// Options: Off, FamilyFirst, ProductFirst, Hybrid
    /// </summary>
    public SemanticSearchMode DefaultMode { get; set; } = SemanticSearchMode.FamilyFirst;

    /// <summary>
    /// Weight for full-text search results in RRF fusion (0.0 to 2.0)
    /// </summary>
    public float FullTextWeight { get; set; } = 1.0f;

    /// <summary>
    /// Weight for semantic/vector search results in RRF fusion (0.0 to 2.0)
    /// </summary>
    public float SemanticWeight { get; set; } = 1.0f;

    /// <summary>
    /// RRF smoothing constant (typically 50-60). Higher values reduce impact of top ranks.
    /// </summary>
    public int RrfK { get; set; } = 50;

    /// <summary>
    /// Maximum number of results to return from hybrid search
    /// </summary>
    public int MatchCount { get; set; } = 10;

    /// <summary>
    /// Minimum similarity threshold for semantic matches (0.0-1.0)
    /// </summary>
    public float SimilarityThreshold { get; set; } = 0.5f;
}
