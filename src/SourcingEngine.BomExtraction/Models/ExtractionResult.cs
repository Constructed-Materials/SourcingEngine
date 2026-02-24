namespace SourcingEngine.BomExtraction.Models;

/// <summary>
/// Complete extraction result for a single BOM file.
/// </summary>
public class ExtractionResult
{
    /// <summary>Original file path that was processed.</summary>
    public string SourceFile { get; set; } = string.Empty;

    /// <summary>UTC timestamp when extraction was performed.</summary>
    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Bedrock model ID used for extraction.</summary>
    public string ModelUsed { get; set; } = string.Empty;

    /// <summary>Number of items successfully extracted.</summary>
    public int ItemCount => Items.Count;

    /// <summary>Extracted BOM line items.</summary>
    public List<BomLineItem> Items { get; set; } = new();

    /// <summary>Warnings encountered during extraction (non-fatal issues).</summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>Input token count from the Bedrock response (for cost tracking).</summary>
    public int? InputTokens { get; set; }

    /// <summary>Output token count from the Bedrock response (for cost tracking).</summary>
    public int? OutputTokens { get; set; }
}
