namespace SourcingEngine.Core.Models;

/// <summary>
/// Represents a Bill of Materials line item to search for
/// </summary>
public record BomItem
{
    /// <summary>
    /// Original input text from the BOM
    /// </summary>
    public required string RawText { get; init; }
    
    /// <summary>
    /// Extracted keywords for searching
    /// </summary>
    public List<string> Keywords { get; init; } = [];
    
    /// <summary>
    /// All size variants (imperial and metric) for searching
    /// </summary>
    public List<string> SizeVariants { get; init; } = [];
    
    /// <summary>
    /// Expanded synonyms for broader matching
    /// </summary>
    public List<string> Synonyms { get; init; } = [];
}
