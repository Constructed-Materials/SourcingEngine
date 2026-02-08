using SourcingEngine.Core.Services;
using Xunit;

namespace SourcingEngine.Tests.Unit;

/// <summary>
/// Unit tests for SizeCalculator - no database required
/// </summary>
public class SizeCalculatorTests
{
    private readonly SizeCalculator _calculator = new();

    [Theory]
    [InlineData("8 inch", "20cm")]
    [InlineData("8\"", "20cm")]
    [InlineData("8 in", "20cm")]
    [InlineData("4 inch", "10cm")]
    [InlineData("12\"", "30cm")]
    public void GetSizeVariants_ImperialInput_ContainsMetricCm(string input, string expectedCm)
    {
        var variants = _calculator.GetSizeVariants(input);
        
        Assert.Contains(variants, v => v.Contains(expectedCm.Replace("cm", ""), StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("8 inch", "203mm")] // 8 * 25.4 = 203.2 → 203
    [InlineData("4 inch", "102mm")] // 4 * 25.4 = 101.6 → 102
    public void GetSizeVariants_ImperialInput_ContainsMetricMm(string input, string expectedMm)
    {
        var variants = _calculator.GetSizeVariants(input);
        
        // Check for the numeric part
        var numericPart = expectedMm.Replace("mm", "");
        Assert.Contains(variants, v => v.Contains(numericPart) && v.Contains("mm", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("20cm", "8")]
    [InlineData("10cm", "4")]
    [InlineData("30cm", "12")]
    public void GetSizeVariants_MetricCmInput_ContainsImperial(string input, string expectedInches)
    {
        var variants = _calculator.GetSizeVariants(input);
        
        Assert.Contains(variants, v => v.Contains(expectedInches) && (v.Contains("\"") || v.Contains("inch", StringComparison.OrdinalIgnoreCase)));
    }

    [Theory]
    [InlineData("200mm", "8")]
    [InlineData("100mm", "4")]
    public void GetSizeVariants_MetricMmInput_ContainsImperial(string input, string expectedInches)
    {
        var variants = _calculator.GetSizeVariants(input);
        
        Assert.Contains(variants, v => v.Contains(expectedInches) && (v.Contains("\"") || v.Contains("inch", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void GetSizeVariants_NoSizeInInput_ReturnsEmpty()
    {
        var variants = _calculator.GetSizeVariants("masonry block");
        
        Assert.Empty(variants);
    }

    [Theory]
    [InlineData("8 inch masonry block", 8, "inch")]
    [InlineData("20cm concrete", 20, "cm")]
    [InlineData("200mm steel", 200, "mm")]
    [InlineData("12ft joist", 12, "ft")]
    [InlineData("4m beam", 4, "m")]
    public void ExtractSize_ValidInput_ReturnsCorrectValues(string input, double expectedValue, string expectedUnit)
    {
        var result = _calculator.ExtractSize(input);
        
        Assert.NotNull(result);
        Assert.Equal(expectedValue, result.Value.Value);
        Assert.Equal(expectedUnit, result.Value.Unit);
    }

    [Theory]
    [InlineData("12 ft", "3.7")]   // 12 * 0.3048 = 3.66 → rounded to 3.7m
    [InlineData("10 feet", "3")]   // 10 * 0.3048 = 3.048 → rounded to 3m
    public void GetSizeVariants_FeetInput_ContainsMeters(string input, string expectedM)
    {
        var variants = _calculator.GetSizeVariants(input);

        Assert.Contains(variants, v => v.Contains(expectedM) && v.Contains("m", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("4 m", "13")]   // 4 / 0.3048 = 13.12 → rounded to 13 ft
    [InlineData("3 meter", "10")] // 3 / 0.3048 = 9.84 → rounded to 10 ft
    public void GetSizeVariants_MetersInput_ContainsFeet(string input, string expectedFt)
    {
        var variants = _calculator.GetSizeVariants(input);

        Assert.Contains(variants, v => v.Contains(expectedFt) && v.Contains("ft", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("100 sqft", "9")]  // 100 * 0.092903 = 9.29 → rounded to 9.3 sqm
    public void GetSizeVariants_SqFtInput_ContainsSqM(string input, string expectedSqM)
    {
        var variants = _calculator.GetSizeVariants(input);

        Assert.Contains(variants, v => v.Contains(expectedSqM) && (v.Contains("sqm") || v.Contains("sq m") || v.Contains("m²")));
    }

    [Theory]
    [InlineData("10 sqm", "108")] // 10 / 0.092903 = 107.6 → rounded to 108 sqft
    public void GetSizeVariants_SqMInput_ContainsSqFt(string input, string expectedSqFt)
    {
        var variants = _calculator.GetSizeVariants(input);

        Assert.Contains(variants, v => v.Contains(expectedSqFt) && (v.Contains("sqft") || v.Contains("sq ft") || v.Contains("sf")));
    }
}
