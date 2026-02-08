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
    /// Model code from vendor schema
    /// </summary>
    [JsonPropertyName("modelCode")]
    public string? ModelCode { get; init; }
    
    /// <summary>
    /// CSI section code - e.g., "042200"
    /// </summary>
    [JsonPropertyName("csiCode")]
    public string? CsiCode { get; init; }
    
    /// <summary>
    /// When to use this product
    /// </summary>
    [JsonPropertyName("useWhen")]
    public string? UseWhen { get; init; }
    
    /// <summary>
    /// Key features as JSON array
    /// </summary>
    [JsonPropertyName("keyFeatures")]
    public List<string>? KeyFeatures { get; init; }
    
    /// <summary>
    /// Technical specifications as JSON object
    /// </summary>
    [JsonPropertyName("technicalSpecs")]
    public Dictionary<string, object>? TechnicalSpecs { get; init; }
    
    /// <summary>
    /// Performance data as JSON object
    /// </summary>
    [JsonPropertyName("performanceData")]
    public Dictionary<string, object>? PerformanceData { get; init; }
    
    /// <summary>
    /// Source schema for this enriched data
    /// </summary>
    [JsonPropertyName("sourceSchema")]
    public string? SourceSchema { get; init; }

    /// <summary>
    /// Semantic similarity score (0.0-1.0) from embedding search.
    /// Only populated when using ProductFirst or Hybrid search mode.
    /// </summary>
    [JsonPropertyName("semanticScore")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? SemanticScore { get; init; }
}
