namespace SourcingEngine.Core.Models;

/// <summary>
/// Encapsulates input parameters for a product search.
/// Replaces multiple nullable primitive parameters on IProductRepository.
/// </summary>
public record ProductSearchRequest
{
    /// <summary>Material family label to filter by (e.g., "cmu_blocks").</summary>
    public string? FamilyLabel { get; init; }

    /// <summary>CSI section code to filter by.</summary>
    public string? CsiCode { get; init; }

    /// <summary>Size patterns for dimensional matching (e.g., ["200mm", "8\""]).</summary>
    public IReadOnlyList<string>? SizePatterns { get; init; }

    /// <summary>Keywords/synonyms for product search.</summary>
    public IReadOnlyList<string>? Keywords { get; init; }
}

/// <summary>
/// Encapsulates input parameters for a semantic vector search.
/// Replaces overloaded methods on ISemanticProductRepository.
/// </summary>
public record SemanticSearchRequest
{
    /// <summary>Query embedding vector.</summary>
    public required float[] QueryEmbedding { get; init; }

    /// <summary>Optional family label filter.</summary>
    public string? FamilyLabel { get; init; }

    /// <summary>Minimum cosine similarity threshold (0.0–1.0).</summary>
    public float MatchThreshold { get; init; } = 0.5f;

    /// <summary>Maximum number of results to return.</summary>
    public int MatchCount { get; init; } = 10;
}
