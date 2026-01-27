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
    public void ExtractSize_ValidInput_ReturnsCorrectValues(string input, double expectedValue, string expectedUnit)
    {
        var result = _calculator.ExtractSize(input);
        
        Assert.NotNull(result);
        Assert.Equal(expectedValue, result.Value.Value);
        Assert.Equal(expectedUnit, result.Value.Unit);
    }
}
