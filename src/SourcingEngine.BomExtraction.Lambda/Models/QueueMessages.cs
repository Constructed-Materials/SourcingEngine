using System.Text.Json.Serialization;
using SourcingEngine.BomExtraction.Models;

namespace SourcingEngine.BomExtraction.Lambda.Models;

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
/// Matches the Python ExtractionResultMessage contract.
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
