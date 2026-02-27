using System.Text.Json.Serialization;

namespace SourcingEngine.Core.Models;

/// <summary>
/// A matched product from the search
/// </summary>
public record ProductMatch
{
    /// <summary>
    /// Product unique identifier
    /// </summary>
    [JsonPropertyName("productId")]
    public Guid ProductId { get; init; }
    
    /// <summary>
    /// Vendor/manufacturer name
    /// </summary>
    [JsonPropertyName("vendor")]
    public required string Vendor { get; init; }
    
    /// <summary>
    /// Product model name from public.products
    /// </summary>
    [JsonPropertyName("modelName")]
    public required string ModelName { get; init; }
    
    /// <summary>
    /// CSI section code - e.g., "042200"
    /// </summary>
    [JsonPropertyName("csiCode")]
    public string? CsiCode { get; init; }
    
    /// <summary>
    /// Product description from public.product_knowledge
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }
    
    /// <summary>
    /// Use cases from public.product_knowledge.use_cases
    /// </summary>
    [JsonPropertyName("useCases")]
    public List<string>? UseCases { get; init; }
    
    /// <summary>
    /// Technical specifications from public.product_knowledge.specifications
    /// </summary>
    [JsonPropertyName("technicalSpecs")]
    public Dictionary<string, object>? TechnicalSpecs { get; init; }

    /// <summary>
    /// Semantic similarity score (0.0-1.0) from embedding search.
    /// Only populated when using ProductFirst or Hybrid search mode.
    /// </summary>
    [JsonPropertyName("semanticScore")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? SemanticScore { get; init; }

    /// <summary>
    /// Final blended score after spec-match re-ranking (0.0-1.0).
    /// Combines semantic similarity with structured specification matching.
    /// Null when re-ranking is disabled or specs are unavailable.
    /// </summary>
    [JsonPropertyName("finalScore")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? FinalScore { get; init; }
}
