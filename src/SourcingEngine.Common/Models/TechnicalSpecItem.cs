using System.Text.Json.Serialization;

namespace SourcingEngine.Common.Models;

/// <summary>
/// A single measured/dimensional specification extracted from a BOM line item.
/// For example: width = 8 in, height = 8 in, thickness = 0.75 in.
/// </summary>
public class TechnicalSpecItem
{
    /// <summary>
    /// Dimension name (e.g. "width", "height", "thickness", "length", "depth", "diameter", "gauge").
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Numeric value of the dimension.
    /// </summary>
    [JsonPropertyName("value")]
    public double? Value { get; set; }

    /// <summary>
    /// Unit of measure for this dimension (e.g. "in", "ft", "cm", "mm", "m").
    /// </summary>
    [JsonPropertyName("uom")]
    public string? Uom { get; set; }
}
