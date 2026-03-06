namespace SourcingEngine.Core.Models;

/// <summary>
/// Holds the three separate text strings used to generate multi-vector embeddings.
/// Each text string maps to a dedicated embedding column in the database and
/// is weighted independently at search time for precise matching.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>DescriptionText</b> (weight 0.6): [MATERIAL] [PRODUCT] [DESCRIPTION] [CERTIFICATIONS] — identity, material, and compliance.</item>
///   <item><b>SpecsText</b> (weight 0.3): [TECHNICALSPECS] — dimensional and physical specifications only.</item>
///   <item><b>EnrichmentText</b> (weight 0.1): [PRODUCTENRICHMENT] — vendor, family, use cases, contextual knowledge.</item>
/// </list>
/// </remarks>
public record MultiVectorEmbeddingText
{
    /// <summary>
    /// Text for Vector A: [MATERIAL] [PRODUCT] [DESCRIPTION] [CERTIFICATIONS].
    /// Captures the product/item identity, material type, and compliance standards.
    /// </summary>
    public required string DescriptionText { get; init; }

    /// <summary>
    /// Text for Vector B: [TECHNICALSPECS].
    /// Captures dimensional and physical specification data only.
    /// </summary>
    public required string SpecsText { get; init; }

    /// <summary>
    /// Text for Vector C: [PRODUCTENRICHMENT].
    /// Captures vendor context, material family, use cases, and additional knowledge.
    /// </summary>
    public required string EnrichmentText { get; init; }

    /// <summary>
    /// Full concatenation of all sections for debug/logging purposes.
    /// Stored in the <c>embedding_text</c> column for traceability.
    /// </summary>
    public required string FullDebugText { get; init; }
}
