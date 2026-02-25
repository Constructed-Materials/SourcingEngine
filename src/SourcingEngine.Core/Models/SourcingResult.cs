using System.Text.Json.Serialization;

namespace SourcingEngine.Core.Models;

/// <summary>
/// Aggregate result for a full BOM extraction — one <see cref="BomItemSearchResult"/> per BOM line item.
/// </summary>
public record SourcingResult
{
    /// <summary>Trace ID carried from the extraction request for end-to-end correlation.</summary>
    [JsonPropertyName("traceId")]
    public string TraceId { get; init; } = string.Empty;

    /// <summary>Project ID this result belongs to.</summary>
    [JsonPropertyName("projectId")]
    public string ProjectId { get; init; } = string.Empty;

    /// <summary>Original source file that was extracted.</summary>
    [JsonPropertyName("sourceFile")]
    public string SourceFile { get; init; } = string.Empty;

    /// <summary>Per-BOM-item search results.</summary>
    [JsonPropertyName("items")]
    public List<BomItemSearchResult> Items { get; init; } = [];

    /// <summary>Total product matches across all BOM items.</summary>
    [JsonPropertyName("totalMatches")]
    public int TotalMatches => Items.Sum(i => i.SearchResult.MatchCount);

    /// <summary>Total execution time in milliseconds for the entire batch.</summary>
    [JsonPropertyName("totalExecutionTimeMs")]
    public long TotalExecutionTimeMs { get; init; }

    /// <summary>Aggregate warnings from all items.</summary>
    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; init; } = [];
}

/// <summary>
/// Search result for a single BOM line item, pairing the original BOM data
/// with matched products.
/// </summary>
public record BomItemSearchResult
{
    /// <summary>Short canonical item name from the BOM extraction.</summary>
    [JsonPropertyName("bomItem")]
    public string BomItemName { get; init; } = string.Empty;

    /// <summary>Full specification text from the BOM extraction.</summary>
    [JsonPropertyName("spec")]
    public string Spec { get; init; } = string.Empty;

    /// <summary>Quantity from the BOM extraction.</summary>
    [JsonPropertyName("quantity")]
    public double? Quantity { get; init; }

    /// <summary>Product search results for this BOM item.</summary>
    [JsonPropertyName("searchResult")]
    public required SearchResult SearchResult { get; init; }
}
