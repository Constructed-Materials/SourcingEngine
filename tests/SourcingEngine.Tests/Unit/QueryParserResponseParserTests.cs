using SourcingEngine.Core.Services;
using Xunit;

namespace SourcingEngine.Tests.Unit;

/// <summary>
/// Unit tests for QueryParserResponseParser (shared LLM response parsing logic).
/// </summary>
public class QueryParserResponseParserTests
{
    [Fact]
    public void Parse_ValidJson_ReturnsSuccessfulResult()
    {
        // Arrange
        var json = @"{""material_family"":""cmu"",""width_inches"":8,""height_inches"":8,""length_inches"":16,""attributes"":{""color"":""gray""},""search_query"":""8 inch CMU gray"",""confidence"":0.95}";

        // Act
        var result = QueryParserResponseParser.Parse(json, "8 inch cmu gray");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("cmu", result.MaterialFamily);
        Assert.Equal(8, result.TechnicalSpecs.WidthInches);
        Assert.Equal(8, result.TechnicalSpecs.HeightInches);
        Assert.Equal(16, result.TechnicalSpecs.LengthInches);
        Assert.Equal("gray", result.Attributes["color"]);
        Assert.Equal("8 inch CMU gray", result.SearchQuery);
        Assert.Equal(0.95f, result.Confidence);
    }

    [Fact]
    public void Parse_JsonWrappedInText_ExtractsSuccessfully()
    {
        // Arrange
        var wrapped = @"Here is the result:
{""material_family"":""rebar"",""search_query"":""rebar steel"",""confidence"":0.9}
Done.";

        // Act
        var result = QueryParserResponseParser.Parse(wrapped, "#5 rebar");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("rebar", result.MaterialFamily);
    }

    [Fact]
    public void Parse_NoJson_ReturnsFailureWithOriginalInputAsFallback()
    {
        // Act
        var result = QueryParserResponseParser.Parse("no json here", "original input");

        // Assert
        Assert.False(result.Success);
        Assert.Equal("original input", result.SearchQuery);
        Assert.Contains("JSON", result.ErrorMessage);
    }

    [Fact]
    public void Parse_MissingBothMaterialAndSearchQuery_ReturnsFailure()
    {
        // Arrange — nested fragment that matches outer regex
        var json = @"{""color"":""gray""}";

        // Act
        var result = QueryParserResponseParser.Parse(json, "gray block");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("missing required fields", result.ErrorMessage);
    }

    [Fact]
    public void Parse_NullSearchQuery_FallsBackToOriginalInput()
    {
        // Arrange — has material_family but no search_query
        var json = @"{""material_family"":""lumber"",""confidence"":0.8}";

        // Act
        var result = QueryParserResponseParser.Parse(json, "2x4 lumber");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("lumber", result.MaterialFamily);
        Assert.Equal("2x4 lumber", result.SearchQuery); // Falls back
    }

    [Fact]
    public void Parse_NullConfidence_DefaultsToHalf()
    {
        var json = @"{""material_family"":""stucco"",""search_query"":""stucco EIFS""}";

        var result = QueryParserResponseParser.Parse(json, "stucco");

        Assert.True(result.Success);
        Assert.Equal(0.5f, result.Confidence);
    }

    [Fact]
    public void Parse_InvalidJsonSyntax_ReturnsFailure()
    {
        var result = QueryParserResponseParser.Parse("{invalid:", "test");

        Assert.False(result.Success);
        // The regex may match "{invalid:" as a fragment; either way it should fail
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void Parse_AllDimensions_ExtractsCorrectly()
    {
        var json = @"{""material_family"":""cmu"",""width_inches"":8,""height_inches"":8,""length_inches"":16,""thickness_inches"":2.5,""diameter_inches"":null,""search_query"":""CMU"",""confidence"":0.9}";

        var result = QueryParserResponseParser.Parse(json, "cmu");

        Assert.True(result.Success);
        Assert.Equal(8, result.TechnicalSpecs.WidthInches);
        Assert.Equal(8, result.TechnicalSpecs.HeightInches);
        Assert.Equal(16, result.TechnicalSpecs.LengthInches);
        Assert.Equal(2.5, result.TechnicalSpecs.ThicknessInches);
        Assert.Null(result.TechnicalSpecs.DiameterInches);
    }

    [Fact]
    public void Parse_DiameterOnly_ExtractsCorrectly()
    {
        var json = @"{""material_family"":""rebar"",""diameter_inches"":0.625,""search_query"":""rebar"",""confidence"":0.98}";

        var result = QueryParserResponseParser.Parse(json, "#5 rebar");

        Assert.True(result.Success);
        Assert.Equal(0.625, result.TechnicalSpecs.DiameterInches);
        Assert.Null(result.TechnicalSpecs.WidthInches);
    }

    [Fact]
    public void BuildOllamaPrompt_ContainsInputText()
    {
        var prompt = QueryParserPrompts.BuildOllamaPrompt("test material");

        Assert.Contains("test material", prompt);
        Assert.Contains("construction materials parser", prompt); // System prompt
        Assert.Contains("OUTPUT:", prompt); // Ends with OUTPUT: marker
    }
}
