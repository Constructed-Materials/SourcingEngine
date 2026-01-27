using System.Text.Json.Serialization;

namespace SourcingEngine.Core.Models;

/// <summary>
/// Complete search result returned as JSON
/// </summary>
public record SearchResult
{
    /// <summary>
    /// Original search query
    /// </summary>
    [JsonPropertyName("query")]
    public required string Query { get; init; }
    
    /// <summary>
    /// All size variants searched
    /// </summary>
    [JsonPropertyName("sizeVariants")]
    public List<string> SizeVariants { get; init; } = [];
    
    /// <summary>
    /// Keywords extracted and expanded
    /// </summary>
    [JsonPropertyName("keywords")]
    public List<string> Keywords { get; init; } = [];
    
    /// <summary>
    /// Resolved material family label
    /// </summary>
    [JsonPropertyName("familyLabel")]
    public string? FamilyLabel { get; init; }
    
    /// <summary>
    /// Resolved CSI section code
    /// </summary>
    [JsonPropertyName("csiCode")]
    public string? CsiCode { get; init; }
    
    /// <summary>
    /// Number of matching products
    /// </summary>
    [JsonPropertyName("matchCount")]
    public int MatchCount => Matches.Count;
    
    /// <summary>
    /// List of matching products with enriched data
    /// </summary>
    [JsonPropertyName("matches")]
    public List<ProductMatch> Matches { get; init; } = [];
    
    /// <summary>
    /// Search execution time in milliseconds
    /// </summary>
    [JsonPropertyName("executionTimeMs")]
    public long ExecutionTimeMs { get; init; }
    
    /// <summary>
    /// Any warnings or errors during search
    /// </summary>
    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; init; } = [];
}
