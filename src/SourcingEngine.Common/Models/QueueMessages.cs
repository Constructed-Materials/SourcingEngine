using System.Text.Json.Serialization;

namespace SourcingEngine.Common.Models;

/// <summary>
/// Reference to a single BOM file in an extraction request.
/// Matches the Python BomFileReference contract (camelCase JSON).
/// </summary>
public class BomFileReference
{
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// Incoming extraction request message from the bom-extraction-queue.
/// Matches the Python ExtractionRequestMessage contract.
/// </summary>
public class ExtractionRequestMessage
{
    [JsonPropertyName("traceId")]
    public string TraceId { get; set; } = string.Empty;

    [JsonPropertyName("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("bomFiles")]
    public List<BomFileReference> BomFiles { get; set; } = new();
}

/// <summary>
/// Result message published per-file to the bom-extraction-result-queue.
/// Shared contract between BomExtraction and SourcingEngine pipelines.
/// </summary>
public class ExtractionResultMessage
{
    [JsonPropertyName("traceId")]
    public string TraceId { get; set; } = string.Empty;

    [JsonPropertyName("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("sourceFile")]
    public string SourceFile { get; set; } = string.Empty;

    [JsonPropertyName("sourceUrl")]
    public string SourceUrl { get; set; } = string.Empty;

    [JsonPropertyName("itemCount")]
    public int ItemCount { get; set; }

    [JsonPropertyName("items")]
    public List<BomLineItem> Items { get; set; } = new();

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();

    [JsonPropertyName("modelUsed")]
    public string ModelUsed { get; set; } = string.Empty;

    [JsonPropertyName("inputTokens")]
    public int? InputTokens { get; set; }

    [JsonPropertyName("outputTokens")]
    public int? OutputTokens { get; set; }
}

// ====================================================================
// Sourcing Engine Search — result messages
// ====================================================================

/// <summary>
/// Published to sourcing-engine-search-results-queue for BOM items that matched products.
/// </summary>
public class SourcingResultMessage
{
    [JsonPropertyName("traceId")]
    public string TraceId { get; set; } = string.Empty;

    [JsonPropertyName("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("sourceFile")]
    public string SourceFile { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<SourcingResultItem> Items { get; set; } = new();

    [JsonPropertyName("totalMatches")]
    public int TotalMatches { get; set; }

    [JsonPropertyName("totalExecutionTimeMs")]
    public long TotalExecutionTimeMs { get; set; }

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Per-BOM-item search result with matched products.
/// </summary>
public class SourcingResultItem
{
    [JsonPropertyName("bomItem")]
    public string BomItem { get; set; } = string.Empty;

    [JsonPropertyName("spec")]
    public string Spec { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public double? Quantity { get; set; }

    [JsonPropertyName("matchCount")]
    public int MatchCount { get; set; }

    [JsonPropertyName("matches")]
    public List<ProductMatchDto> Matches { get; set; } = new();

    [JsonPropertyName("familyLabel")]
    public string? FamilyLabel { get; set; }

    [JsonPropertyName("csiCode")]
    public string? CsiCode { get; set; }

    [JsonPropertyName("executionTimeMs")]
    public long ExecutionTimeMs { get; set; }

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Flat product match DTO for queue serialization.
/// </summary>
public class ProductMatchDto
{
    [JsonPropertyName("productId")]
    public Guid ProductId { get; set; }

    [JsonPropertyName("vendor")]
    public string Vendor { get; set; } = string.Empty;

    [JsonPropertyName("modelName")]
    public string ModelName { get; set; } = string.Empty;

    [JsonPropertyName("modelCode")]
    public string? ModelCode { get; set; }

    [JsonPropertyName("csiCode")]
    public string? CsiCode { get; set; }

    [JsonPropertyName("useWhen")]
    public string? UseWhen { get; set; }

    [JsonPropertyName("keyFeatures")]
    public List<string>? KeyFeatures { get; set; }

    [JsonPropertyName("semanticScore")]
    public float? SemanticScore { get; set; }

    [JsonPropertyName("finalScore")]
    public float? FinalScore { get; set; }

    [JsonPropertyName("technicalSpecs")]
    public Dictionary<string, object>? TechnicalSpecs { get; set; }

    [JsonPropertyName("sourceSchema")]
    public string? SourceSchema { get; set; }
}

/// <summary>
/// Published to sourcing-engine-search-zero-results-queue for BOM items with no product matches.
/// </summary>
public class SourcingZeroResultsMessage
{
    [JsonPropertyName("traceId")]
    public string TraceId { get; set; } = string.Empty;

    [JsonPropertyName("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("sourceFile")]
    public string SourceFile { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<ZeroResultItem> Items { get; set; } = new();

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// A BOM item that returned zero product matches during search.
/// </summary>
public class ZeroResultItem
{
    [JsonPropertyName("bomItem")]
    public string BomItem { get; set; } = string.Empty;

    [JsonPropertyName("spec")]
    public string Spec { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public double? Quantity { get; set; }

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();
}
