using System.Globalization;
using System.Text.Json;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Converts between imperial and metric units for construction material dimensions.
/// Used by <see cref="ProductEmbeddingTextBuilder"/> and <see cref="SpecMatchReRanker"/>
/// to ensure multi-unit representation in embedding text and dimensional matching.
/// Family-agnostic — detects units from spec key suffixes (_mm, _inches, _cm, etc.)
/// </summary>
public static class DimensionUnitConverter
{
    private const double MmPerInch = 25.4;
    private const double MmPerCm = 10.0;
    private const double MmPerFoot = 304.8;

    /// <summary>
    /// Known unit suffixes and their canonical unit names.
    /// Used to detect measurement units from spec key names across all product families.
    /// </summary>
    private static readonly (string Suffix, string Unit)[] UnitSuffixes =
    {
        ("_mm", "mm"),
        ("_inches", "in"),
        ("_inch", "in"),
        ("_in", "in"),
        ("_cm", "cm"),
        ("_ft", "ft"),
        ("_feet", "ft"),
        ("_m", "m"),
    };

    /// <summary>
    /// Spec keys that are dimensional even without a unit suffix.
    /// Maps key name → assumed unit.
    /// </summary>
    private static readonly Dictionary<string, string> DimensionalKeyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["width"] = "in",
        ["height"] = "in",
        ["length"] = "in",
        ["depth"] = "in",
        ["thickness"] = "in",
        ["diameter"] = "in",
    };

    /// <summary>
    /// Spec keys that contain units by convention but should NOT be multi-unit converted.
    /// These are performance / rating specs, not physical dimensions.
    /// </summary>
    private static readonly HashSet<string> NonDimensionalKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "u_factor", "u_factor_max", "u_factor_min", "u_factor_range",
        "uf_w_m2k", "uf_range", "uw_w_m2k", "uw_range",
        "shgc", "shgc_max", "shgc_min", "shgc_range",
        "r_value", "r_value_min", "r_value_max",
        "stc", "stc_min", "stc_max",
        "oitc", "oitc_min", "oitc_max",
        "design_pressure_psf", "overload_psf",
        "water_pa", "water_resistance_psf",
        "air_permeance", "air_class", "wind_class", "water_class",
        "condensation_resistance_min", "condensation_resistance_max",
        "density_kg_m3",
        "tensile_strength", "elongation", "coverage_rate",
        "cure_time", "rain_resistance", "min_application_temp",
        "dry_film_thickness",
        "unit_number", "cycle_rating",
    };

    /// <summary>
    /// Detect if a spec key represents a physical dimension with a unit suffix.
    /// Returns the base key name and detected unit, or null if not dimensional.
    /// </summary>
    public static (string BaseKey, string Unit)? DetectUnit(string specKey)
    {
        if (string.IsNullOrWhiteSpace(specKey))
            return null;

        var key = specKey.Trim().ToLowerInvariant();

        // Skip known non-dimensional keys
        if (NonDimensionalKeys.Contains(key))
            return null;

        // Check for unit suffix
        foreach (var (suffix, unit) in UnitSuffixes)
        {
            if (key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var baseKey = key[..^suffix.Length];
                if (!string.IsNullOrEmpty(baseKey))
                    return (baseKey, unit);
            }
        }

        // Check dimensionless keys with implied units
        if (DimensionalKeyMap.TryGetValue(key, out var impliedUnit))
            return (key, impliedUnit);

        return null;
    }

    /// <summary>
    /// Convert a dimensional value to all common unit representations for embedding.
    /// Returns formatted strings like "width: 190 mm (7.5 in)" for each value.
    /// Handles scalar and array JSON values.
    /// </summary>
    public static List<string> FormatDimensionMultiUnit(string baseKey, string unit, double value)
    {
        var results = new List<string>();
        var mm = ToMillimeters(value, unit);

        if (mm <= 0)
            return results;

        var inches = mm / MmPerInch;
        var cm = mm / MmPerCm;

        // Primary format: original unit with conversion
        if (unit == "mm")
        {
            results.Add($"{baseKey}: {FormatNumber(mm)} mm ({FormatNumber(inches)} in)");
        }
        else if (unit == "in")
        {
            results.Add($"{baseKey}: {FormatNumber(inches)} in ({FormatNumber(mm)} mm)");
        }
        else if (unit == "cm")
        {
            results.Add($"{baseKey}: {FormatNumber(cm)} cm ({FormatNumber(inches)} in) ({FormatNumber(mm)} mm)");
        }
        else if (unit == "ft")
        {
            var feet = mm / MmPerFoot;
            results.Add($"{baseKey}: {FormatNumber(feet)} ft ({FormatNumber(mm)} mm)");
        }
        else
        {
            results.Add($"{baseKey}: {FormatNumber(value)} {unit}");
        }

        return results;
    }

    /// <summary>
    /// Convert a value from its current unit to millimeters (canonical unit for comparison).
    /// </summary>
    public static double ToMillimeters(double value, string unit)
    {
        return unit.ToLowerInvariant() switch
        {
            "mm" => value,
            "cm" => value * MmPerCm,
            "in" or "inch" or "inches" => value * MmPerInch,
            "ft" or "feet" or "foot" => value * MmPerFoot,
            "m" => value * 1000.0,
            _ => value // assume mm if unknown
        };
    }

    /// <summary>
    /// Parse a dimension string like "8 in" or "200 mm" into (value, unit).
    /// Returns null if parsing fails.
    /// </summary>
    public static (double Value, string Unit)? ParseDimensionString(string? dimensionStr)
    {
        if (string.IsNullOrWhiteSpace(dimensionStr))
            return null;

        var trimmed = dimensionStr.Trim();

        // Match patterns like "8 in", "200mm", "8.5 inches", "20 cm"
        var match = System.Text.RegularExpressions.Regex.Match(
            trimmed,
            @"^(\d+(?:\.\d+)?)\s*(mm|cm|in|inch|inches|ft|feet|foot|m)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (match.Success)
        {
            if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
            {
                var unit = NormalizeUnit(match.Groups[2].Value);
                return (val, unit);
            }
        }

        return null;
    }

    /// <summary>
    /// Normalize unit strings to canonical short forms.
    /// </summary>
    public static string NormalizeUnit(string unit)
    {
        return unit.ToLowerInvariant() switch
        {
            "inches" or "inch" => "in",
            "feet" or "foot" => "ft",
            _ => unit.ToLowerInvariant()
        };
    }

    /// <summary>
    /// Format a product spec JSON object (any family) into pipe-delimited spec entries
    /// with multi-unit conversion for dimensional values.
    /// Handles: scalar numbers, scalar strings, arrays of numbers, arrays of strings, booleans.
    /// </summary>
    public static List<string> FormatSpecsFromJsonObject(Dictionary<string, JsonElement>? specs)
    {
        if (specs == null || specs.Count == 0)
            return new List<string>();

        var entries = new List<string>();

        foreach (var (key, element) in specs)
        {
            var formatted = FormatSpecEntry(key, element);
            entries.AddRange(formatted);
        }

        return entries;
    }

    /// <summary>
    /// Format a single spec entry, applying multi-unit conversion when appropriate.
    /// </summary>
    internal static List<string> FormatSpecEntry(string key, JsonElement element)
    {
        var results = new List<string>();
        var detected = DetectUnit(key);

        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                if (detected != null && element.TryGetDouble(out var numVal))
                {
                    results.AddRange(FormatDimensionMultiUnit(detected.Value.BaseKey, detected.Value.Unit, numVal));
                }
                else
                {
                    var readable = FormatKeyReadable(key);
                    results.Add($"{readable}: {element.GetRawText()}");
                }
                break;

            case JsonValueKind.String:
                var strVal = element.GetString();
                if (!string.IsNullOrWhiteSpace(strVal))
                {
                    // Try to parse dimensional strings like "8 in" even without suffix
                    if (detected != null)
                    {
                        var parsed = ParseDimensionString(strVal);
                        if (parsed != null)
                        {
                            results.AddRange(FormatDimensionMultiUnit(
                                detected.Value.BaseKey, parsed.Value.Unit, parsed.Value.Value));
                        }
                        else
                        {
                            results.Add($"{FormatKeyReadable(key)}: {strVal}");
                        }
                    }
                    else
                    {
                        results.Add($"{FormatKeyReadable(key)}: {strVal}");
                    }
                }
                break;

            case JsonValueKind.True:
                results.Add($"{FormatKeyReadable(key)}: yes");
                break;

            case JsonValueKind.False:
                results.Add($"{FormatKeyReadable(key)}: no");
                break;

            case JsonValueKind.Array:
                results.AddRange(FormatArraySpecEntry(key, element, detected));
                break;
        }

        return results;
    }

    /// <summary>
    /// Format an array spec entry. For numeric arrays of dimensional values, emit
    /// each value with multi-unit conversion. For string arrays, join them.
    /// </summary>
    private static List<string> FormatArraySpecEntry(
        string key, JsonElement element,
        (string BaseKey, string Unit)? detected)
    {
        var results = new List<string>();
        var items = new List<string>();
        var hasNumeric = false;

        foreach (var arrayItem in element.EnumerateArray())
        {
            if (arrayItem.ValueKind == JsonValueKind.Number && detected != null)
            {
                hasNumeric = true;
                if (arrayItem.TryGetDouble(out var val))
                {
                    results.AddRange(FormatDimensionMultiUnit(
                        detected.Value.BaseKey, detected.Value.Unit, val));
                }
            }
            else if (arrayItem.ValueKind == JsonValueKind.String)
            {
                var s = arrayItem.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    items.Add(s);
            }
            else if (arrayItem.ValueKind == JsonValueKind.Number)
            {
                items.Add(arrayItem.GetRawText());
            }
        }

        if (!hasNumeric && items.Count > 0)
        {
            results.Add($"{FormatKeyReadable(key)}: {string.Join(", ", items)}");
        }

        return results;
    }

    /// <summary>
    /// Convert a snake_case key to a readable format for embedding text.
    /// Strips unit suffixes to avoid redundancy (e.g., "width_mm" → "width").
    /// </summary>
    internal static string FormatKeyReadable(string key)
    {
        var readable = key.ToLowerInvariant();

        // Strip unit suffixes for cleaner embedding text
        foreach (var (suffix, _) in UnitSuffixes)
        {
            if (readable.EndsWith(suffix))
            {
                readable = readable[..^suffix.Length];
                break;
            }
        }

        return readable.Replace("_", " ");
    }

    private static string FormatNumber(double value)
    {
        // Use compact formatting: "190" for whole numbers, "7.5" for decimals
        return value == Math.Floor(value)
            ? value.ToString("F0", CultureInfo.InvariantCulture)
            : value.ToString("F1", CultureInfo.InvariantCulture);
    }
}
