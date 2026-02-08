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
    /// Extracted dimensions normalized to standard format
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
/// Technical specifications extracted from BOM line item
/// </summary>
public class TechnicalSpecs
{
    /// <summary>
    /// Width in inches (null if not specified)
    /// </summary>
    public double? WidthInches { get; set; }

    /// <summary>
    /// Height in inches (null if not specified)
    /// </summary>
    public double? HeightInches { get; set; }

    /// <summary>
    /// Length/Depth in inches (null if not specified)
    /// </summary>
    public double? LengthInches { get; set; }

    /// <summary>
    /// Thickness in inches (null if not specified)
    /// </summary>
    public double? ThicknessInches { get; set; }

    /// <summary>
    /// Diameter in inches (null if not specified)
    /// </summary>
    public double? DiameterInches { get; set; }

    /// <summary>
    /// Formatted string for embedding: "8x8x16in" or "4in thick"
    /// </summary>
    public string ToEmbeddingFormat()
    {
        var parts = new List<string>();

        if (WidthInches.HasValue && HeightInches.HasValue && LengthInches.HasValue)
        {
            parts.Add($"{WidthInches}x{HeightInches}x{LengthInches}in");
        }
        else if (WidthInches.HasValue && HeightInches.HasValue)
        {
            parts.Add($"{WidthInches}x{HeightInches}in");
        }
        else
        {
            if (WidthInches.HasValue) parts.Add($"{WidthInches}in wide");
            if (HeightInches.HasValue) parts.Add($"{HeightInches}in high");
            if (LengthInches.HasValue) parts.Add($"{LengthInches}in long");
        }

        if (ThicknessInches.HasValue) parts.Add($"{ThicknessInches}in thick");
        if (DiameterInches.HasValue) parts.Add($"{DiameterInches}in diameter");

        return string.Join(" ", parts);
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
