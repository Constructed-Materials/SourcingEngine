namespace SourcingEngine.Core.Services;

/// <summary>
/// Result from parsing a BOM line item using LLM
/// </summary>
public class ParsedBomQuery
{
    /// <summary>
    /// The primary material/product family (e.g., "cmu", "floor_joist", "stucco")
    /// </summary>
    public string? MaterialFamily { get; set; }

    /// <summary>
    /// Generic technical specifications extracted from the BOM item.
    /// Key-value pairs like "width" → "8 in", "u_factor" → "0.30", "r_value" → "R-19".
    /// Product-type agnostic — supports any material category.
    /// </summary>
    public TechnicalSpecs TechnicalSpecs { get; set; } = new();

    /// <summary>
    /// Product attributes (color, grade, finish, etc.)
    /// </summary>
    public Dictionary<string, string> Attributes { get; set; } = new();

    /// <summary>
    /// Constructed search query for semantic search
    /// </summary>
    public string SearchQuery { get; set; } = string.Empty;

    /// <summary>
    /// Original input text
    /// </summary>
    public string OriginalInput { get; set; } = string.Empty;

    /// <summary>
    /// Confidence score from the LLM (0.0-1.0)
    /// </summary>
    public float Confidence { get; set; } = 0.0f;

    /// <summary>
    /// Whether parsing succeeded
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if parsing failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Generic technical specifications extracted from a BOM line item.
/// Uses a flexible dictionary to support any material type (windows, CMU, insulation, etc.)
/// without hardcoded dimension fields.
/// </summary>
public class TechnicalSpecs
{
    /// <summary>
    /// Key-value specification pairs. Keys are lowercase spec names (e.g., "width", "height",
    /// "u_factor", "r_value", "diameter"), values include units (e.g., "8 in", "0.30", "R-19").
    /// </summary>
    public Dictionary<string, string> Specs { get; set; } = new();

    /// <summary>
    /// Formatted string for embedding, aligned with ProductEmbeddingTextBuilder output.
    /// Format: "width: 8 in | height: 8 in | length: 16 in" or "diameter: 0.625 in | size: #5"
    /// </summary>
    public string ToEmbeddingFormat()
    {
        if (Specs.Count == 0)
            return string.Empty;

        var parts = Specs.Select(kv => $"{kv.Key}: {kv.Value}");
        return string.Join(" | ", parts);
    }
}

/// <summary>
/// Service for parsing BOM line items into structured queries using LLM
/// </summary>
public interface IQueryParserService
{
    /// <summary>
    /// Parse a BOM line item and extract structured information
    /// </summary>
    /// <param name="bomLineItem">Raw BOM text (e.g., "8 inch concrete masonry unit gray")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Parsed query with extracted material, dimensions, and attributes</returns>
    Task<ParsedBomQuery> ParseAsync(string bomLineItem, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if the LLM service is available
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}
