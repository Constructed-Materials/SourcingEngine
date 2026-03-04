using System.Text.Json;
using System.Text.Json.Serialization;

namespace SourcingEngine.Common.Models;

/// <summary>
/// A single measured/dimensional specification extracted from a BOM line item or product.
/// For example: width = 8 in, height = 8 in, thickness = 0.75 in.
/// Value can be a number (double), boolean, or string.
/// </summary>
public class TechnicalSpecItem
{
    /// <summary>
    /// Dimension name (e.g. "width", "height", "thickness", "length", "depth", "diameter", "gauge").
    /// Always uses spaces instead of underscores. Unit suffixes are stripped.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Value of the specification. Can be:
    /// - double (e.g. 290.0, 8, 18.5)
    /// - bool (e.g. true, false)
    /// - string (e.g. "stick", "flamed")
    /// - null if unset
    /// </summary>
    [JsonPropertyName("value")]
    [JsonConverter(typeof(TechnicalSpecValueConverter))]
    public object? Value { get; set; }

    /// <summary>
    /// Unit of measure for this dimension (e.g. "in", "ft", "cm", "mm", "m").
    /// Null for boolean or non-dimensional specs.
    /// </summary>
    [JsonPropertyName("uom")]
    public string? Uom { get; set; }
}

/// <summary>
/// Custom JSON converter for <see cref="TechnicalSpecItem.Value"/> that preserves
/// numeric, boolean, and string types during serialization/deserialization.
/// Without this, System.Text.Json deserializes <c>object?</c> as <see cref="JsonElement"/>.
/// </summary>
public class TechnicalSpecValueConverter : JsonConverter<object?>
{
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Number => reader.GetDouble(),
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Null => null,
            _ => null
        };
    }

    public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case double d:
                // Write whole numbers without decimal point for clean output
                if (d == Math.Floor(d) && !double.IsInfinity(d))
                    writer.WriteNumberValue((long)d);
                else
                    writer.WriteNumberValue(d);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }
}
