using System.Text.Json.Serialization;

namespace SourcingEngine.Common.Models;

/// <summary>
/// A single structured line item extracted from a BOM document.
/// Shared contract between BomExtraction and SourcingEngine pipelines.
/// </summary>
public class BomLineItem
{
    /// <summary>
    /// Short canonical item name (e.g. "Masonry Block", "Plywood Subfloor").
    /// </summary>
    [JsonPropertyName("bom_item")]
    public string BomItem { get; set; } = string.Empty;

    /// <summary>
    /// Full description/specification text useful for product search
    /// (e.g. "8 inch standard weight Masonry Block", "3/4 inch CDX Plywood 4x8 sheet").
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Numeric quantity if present in the BOM, otherwise null.
    /// </summary>
    [JsonPropertyName("quantity")]
    public double? Quantity { get; set; }

    /// <summary>
    /// Unit of measure for the quantity (e.g. "EA", "SQ FT", "LF", "FT").
    /// </summary>
    [JsonPropertyName("uom")]
    public string? Uom { get; set; }

    /// <summary>
    /// BOM category/section the item belongs to (e.g. "Masonry", "Framing", "Roofing").
    /// </summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>
    /// Primary construction material of the item (e.g. "concrete", "steel", "vinyl",
    /// "fiberglass", "wood", "aluminum", "glass"). Extracted by the BOM extraction LLM.
    /// Falls back to <c>ParsedBomQuery.Attributes["material"]</c> at search time if null.
    /// </summary>
    [JsonPropertyName("material")]
    public string? Material { get; set; }

    /// <summary>
    /// Measured/dimensional specifications extracted from the BOM line item.
    /// Each entry has a name, value, and unit (e.g. width = 8 in).
    /// </summary>
    [JsonPropertyName("technical_specs")]
    public List<TechnicalSpecItem>? TechnicalSpecs { get; set; }

    /// <summary>
    /// Certification and compliance standards found on the item
    /// (e.g. "ASTM C90", "LEED v5", "UL Listed").
    /// </summary>
    [JsonPropertyName("certifications")]
    public List<string>? Certifications { get; set; }

    /// <summary>
    /// Additional descriptive notes or remarks from the BOM row
    /// (e.g. "Load-bearing walls only", "Verify field dimensions").
    /// </summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>
    /// Dynamic properties extracted from the BOM row.
    /// Common keys: unit_price, extended_total, brand, grade, finish, color, origin.
    /// </summary>
    [JsonPropertyName("additional_data")]
    public Dictionary<string, object?> AdditionalData { get; set; } = new();
}
