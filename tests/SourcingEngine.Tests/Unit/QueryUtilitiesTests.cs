using SourcingEngine.Core.Services;
using Xunit;

namespace SourcingEngine.Tests.Unit;

/// <summary>
/// Unit tests for QueryUtilities static helpers.
/// No mocks needed — pure functions.
/// </summary>
public class QueryUtilitiesTests
{
    // ── CleanQueryForFts ───────────────────────────────────────────

    [Theory]
    [InlineData("8\" cmu block", "cmu block")]
    [InlineData("8 inch masonry block", "masonry block")]
    [InlineData("200mm concrete block", "concrete block")]
    [InlineData("20cm block", "block")]
    public void CleanQueryForFts_RemovesSizePatterns(string input, string expected)
    {
        var result = QueryUtilities.CleanQueryForFts(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("8x8x16 cmu", "cmu")]
    [InlineData("4'x8' plywood", "' plywood")]
    public void CleanQueryForFts_RemovesDimensionPatterns(string input, string expected)
    {
        var result = QueryUtilities.CleanQueryForFts(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("5/8\" drywall", "drywall")]
    [InlineData("1/2 inch plywood", "/ plywood")]
    public void CleanQueryForFts_RemovesFractions(string input, string expected)
    {
        var result = QueryUtilities.CleanQueryForFts(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("stucco for exterior walls", "stucco exterior walls")]
    [InlineData("insulation on roof", "insulation roof")]
    [InlineData("tiles with grout", "tiles grout")]
    public void CleanQueryForFts_RemovesContextWords(string input, string expected)
    {
        var result = QueryUtilities.CleanQueryForFts(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CleanQueryForFts_NullInput_ReturnsNull()
    {
        Assert.Null(QueryUtilities.CleanQueryForFts(null!));
    }

    [Fact]
    public void CleanQueryForFts_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal("", QueryUtilities.CleanQueryForFts(""));
    }

    [Fact]
    public void CleanQueryForFts_WhitespaceOnly_ReturnsOriginal()
    {
        // IsNullOrWhiteSpace returns early with the original string
        Assert.Equal("   ", QueryUtilities.CleanQueryForFts("   "));
    }

    [Fact]
    public void CleanQueryForFts_PureTextQuery_PassesThroughUnchanged()
    {
        var result = QueryUtilities.CleanQueryForFts("curtain wall aluminum thermal break");
        Assert.Equal("curtain wall aluminum thermal break", result);
    }

    // ── ParseJsonArray ─────────────────────────────────────────────

    [Fact]
    public void ParseJsonArray_ValidJson_ReturnsList()
    {
        var result = QueryUtilities.ParseJsonArray("[\"a\",\"b\",\"c\"]");
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal("a", result[0]);
    }

    [Fact]
    public void ParseJsonArray_NullInput_ReturnsNull()
    {
        Assert.Null(QueryUtilities.ParseJsonArray(null));
    }

    [Fact]
    public void ParseJsonArray_EmptyString_ReturnsNull()
    {
        Assert.Null(QueryUtilities.ParseJsonArray(""));
    }

    [Fact]
    public void ParseJsonArray_InvalidJson_ReturnsNull()
    {
        Assert.Null(QueryUtilities.ParseJsonArray("not json"));
    }

    // ── ParseJsonObject ────────────────────────────────────────────

    [Fact]
    public void ParseJsonObject_ValidJson_ReturnsDictionary()
    {
        var result = QueryUtilities.ParseJsonObject("{\"key\":\"value\"}");
        Assert.NotNull(result);
        Assert.True(result.ContainsKey("key"));
    }

    [Fact]
    public void ParseJsonObject_NullInput_ReturnsNull()
    {
        Assert.Null(QueryUtilities.ParseJsonObject(null));
    }

    [Fact]
    public void ParseJsonObject_InvalidJson_ReturnsNull()
    {
        Assert.Null(QueryUtilities.ParseJsonObject("{broken"));
    }

    // ── CreateProductMatch ─────────────────────────────────────────

    [Fact]
    public void CreateProductMatch_PopulatesBaseFields()
    {
        var product = new SourcingEngine.Core.Models.Product
        {
            ProductId = Guid.NewGuid(),
            VendorName = "Kawneer",
            ModelName = "1600UT",
            CsiSectionCode = "084113"
        };

        var match = QueryUtilities.CreateProductMatch(product);

        Assert.Equal(product.ProductId, match.ProductId);
        Assert.Equal("Kawneer", match.Vendor);
        Assert.Equal("1600UT", match.ModelName);
        Assert.Equal("084113", match.CsiCode);
    }

    [Fact]
    public void CreateProductMatch_HasNullOptionalFields()
    {
        var product = new SourcingEngine.Core.Models.Product
        {
            ProductId = Guid.NewGuid(),
            VendorName = "Acme",
            ModelName = "Basic",
            CsiSectionCode = null
        };

        var match = QueryUtilities.CreateProductMatch(product);

        Assert.Equal("Acme", match.Vendor);
        Assert.Null(match.Description);
        Assert.Null(match.UseCases);
        Assert.Null(match.TechnicalSpecs);
    }
}
