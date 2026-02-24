using System.Text.Json.Serialization;

namespace SourcingEngine.BomExtraction.Models;

/// <summary>
/// A single structured line item extracted from a BOM document.
/// Maps directly to the Python BomLineItem for downstream SourcingEngine compatibility.
/// </summary>
public class BomLineItem
{
    /// <summary>
    /// Short canonical item name (e.g. "Masonry Block", "Plywood Subfloor").
    /// </summary>
    [JsonPropertyName("bom_item")]
    public string BomItem { get; set; } = string.Empty;

    /// <summary>
    /// Full specification text useful for product search
    /// (e.g. "8 inch Masonry Block", "2x4 truss bracing 740 LF").
    /// </summary>
    [JsonPropertyName("spec")]
    public string Spec { get; set; } = string.Empty;

    /// <summary>
    /// Numeric quantity if present in the BOM, otherwise null.
    /// </summary>
    [JsonPropertyName("quantity")]
    public double? Quantity { get; set; }

    /// <summary>
    /// Dynamic properties extracted from the BOM row.
    /// Common keys: section, uom, unit_price, extended_total, notes, dimensions, brand, grade.
    /// </summary>
    [JsonPropertyName("additional_data")]
    public Dictionary<string, object?> AdditionalData { get; set; } = new();
}
