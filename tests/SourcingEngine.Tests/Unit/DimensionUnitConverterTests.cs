using SourcingEngine.Core.Services;
using System.Text.Json;
using Xunit;

namespace SourcingEngine.Tests.Unit;

/// <summary>
/// Unit tests for DimensionUnitConverter — verifies multi-unit conversion,
/// spec key detection, and JSON-based spec formatting across all product families.
/// </summary>
public class DimensionUnitConverterTests
{
    // ── DetectUnit ─────────────────────────────────────────────────

    [Theory]
    [InlineData("width_mm", "mm")]
    [InlineData("height_mm", "mm")]
    [InlineData("thickness_mm", "mm")]
    [InlineData("width_in", "in")]
    [InlineData("height_inches", "in")]
    [InlineData("length_cm", "cm")]
    [InlineData("depth_ft", "ft")]
    public void DetectUnit_RecognizesUnitSuffixes(string key, string expectedUnit)
    {
        var result = DimensionUnitConverter.DetectUnit(key);
        Assert.NotNull(result);
        Assert.Equal(expectedUnit, result.Value.Unit);
    }

    [Theory]
    [InlineData("u_factor")]
    [InlineData("shgc")]
    [InlineData("r_value")]
    [InlineData("weight_kg")]
    [InlineData("color")]
    [InlineData("material")]
    public void DetectUnit_ReturnsNull_ForNonDimensionalKeys(string key)
    {
        Assert.Null(DimensionUnitConverter.DetectUnit(key));
    }

    // ── ToMillimeters ──────────────────────────────────────────────

    [Theory]
    [InlineData(203.2, "mm", 203.2)]      // mm → mm (identity)
    [InlineData(8.0, "in", 203.2)]         // 8 in → 203.2 mm
    [InlineData(20.0, "cm", 200.0)]        // 20 cm → 200 mm
    [InlineData(2.0, "ft", 609.6)]         // 2 ft → 609.6 mm
    public void ToMillimeters_ConvertsCorrectly(double value, string fromUnit, double expectedMm)
    {
        Assert.Equal(expectedMm, DimensionUnitConverter.ToMillimeters(value, fromUnit), 1);
    }

    // ── FormatDimensionMultiUnit ───────────────────────────────────

    [Fact]
    public void FormatDimensionMultiUnit_MmToInch()
    {
        var result = DimensionUnitConverter.FormatDimensionMultiUnit("width", "mm", 203.2);
        // Returns a list like ["width: 203.2 mm (8 in)"]
        Assert.NotEmpty(result);
        var joined = string.Join(" ", result);
        Assert.Contains("203", joined);    // mm value
        Assert.Contains("8", joined);      // inch conversion (~8.0)
        Assert.Contains("width", joined);
    }

    [Fact]
    public void FormatDimensionMultiUnit_InchToMm()
    {
        var result = DimensionUnitConverter.FormatDimensionMultiUnit("width", "in", 8.0);
        var joined = string.Join(" ", result);
        Assert.Contains("8", joined);        // inch value
        Assert.Contains("203", joined);      // mm conversion
    }

    // ── ParseDimensionString ───────────────────────────────────────

    [Theory]
    [InlineData("8 in", 8.0, "in")]
    [InlineData("203.2mm", 203.2, "mm")]
    [InlineData("20 cm", 20.0, "cm")]
    [InlineData("2.5 ft", 2.5, "ft")]
    [InlineData("200 mm", 200.0, "mm")]
    public void ParseDimensionString_ExtractsValueAndUnit(string input, double expectedValue, string expectedUnit)
    {
        var result = DimensionUnitConverter.ParseDimensionString(input);
        Assert.NotNull(result);
        Assert.Equal(expectedValue, result.Value.Value, 1);
        Assert.Equal(expectedUnit, result.Value.Unit);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("standard")]
    [InlineData("")]
    public void ParseDimensionString_ReturnsNull_ForNonDimensional(string input)
    {
        Assert.Null(DimensionUnitConverter.ParseDimensionString(input));
    }

    // ── FormatSpecsFromJsonObject (cross-family) ───────────────────

    [Fact]
    public void FormatSpecsFromJsonObject_CmuBlock_WidthOptions()
    {
        // Real CMU product spec format from DB
        var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""
        {
            "height_mm": 190,
            "length_mm": 390,
            "width_mm_options": [90, 140, 190, 240, 290],
            "compressive_strength_mpa": 15
        }
        """)!;

        var result = DimensionUnitConverter.FormatSpecsFromJsonObject(json);
        var joined = string.Join(" | ", result);
        Assert.Contains("height", joined);
        Assert.Contains("190", joined);      // mm
        Assert.Contains("width", joined);
        Assert.Contains("90", joined);
        Assert.Contains("290", joined);
    }

    [Fact]
    public void FormatSpecsFromJsonObject_AluminumWindow_MixedSpecs()
    {
        var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""
        {
            "u_factor": 0.30,
            "shgc": 0.25,
            "frame_depth_mm": 165,
            "max_width_mm": 3048,
            "max_height_mm": 3658
        }
        """)!;

        var result = DimensionUnitConverter.FormatSpecsFromJsonObject(json);
        var joined = string.Join(" | ", result);
        // Non-dimensional specs should be included as-is
        Assert.Contains("u factor", joined);
        Assert.Contains("0.3", joined);
        // Dimensional specs should get multi-unit
        Assert.Contains("frame_depth", joined);
        Assert.Contains("165", joined);      // mm
    }

    [Fact]
    public void FormatSpecsFromJsonObject_StoneTile_StringArray()
    {
        var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""
        {
            "thickness_mm": 10,
            "available_sizes": ["60x60", "30x60", "30x30"],
            "material": "porcelain"
        }
        """)!;

        var result = DimensionUnitConverter.FormatSpecsFromJsonObject(json);
        var joined = string.Join(" | ", result);
        Assert.Contains("thickness", joined);
        Assert.Contains("10", joined);
        Assert.Contains("60x60", joined);
        Assert.Contains("porcelain", joined);
    }

    [Fact]
    public void FormatSpecsFromJsonObject_FiberCement_BooleanSpec()
    {
        var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""
        {
            "thickness_mm": 8,
            "obc_compliant": true,
            "fire_rating": "Class A"
        }
        """)!;

        var result = DimensionUnitConverter.FormatSpecsFromJsonObject(json);
        var joined = string.Join(" | ", result);
        Assert.Contains("obc compliant", joined);
        Assert.Contains("yes", joined);
        Assert.Contains("fire rating", joined);
        Assert.Contains("Class A", joined);
    }
}
