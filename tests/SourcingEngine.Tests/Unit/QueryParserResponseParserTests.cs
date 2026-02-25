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
        var json = @"{""material_family"":""cmu"",""technical_specs"":{""width"":""8 in"",""height"":""8 in"",""length"":""16 in""},""attributes"":{""color"":""gray""},""search_query"":""8 inch CMU gray"",""confidence"":0.95}";

        // Act
        var result = QueryParserResponseParser.Parse(json, "8 inch cmu gray");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("cmu", result.MaterialFamily);
        Assert.Equal("8 in", result.TechnicalSpecs.Specs["width"]);
        Assert.Equal("8 in", result.TechnicalSpecs.Specs["height"]);
        Assert.Equal("16 in", result.TechnicalSpecs.Specs["length"]);
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
    public void Parse_MultipleSpecs_ExtractsCorrectly()
    {
        var json = @"{""material_family"":""cmu"",""technical_specs"":{""width"":""8 in"",""height"":""8 in"",""length"":""16 in"",""thickness"":""2.5 in""},""search_query"":""CMU"",""confidence"":0.9}";

        var result = QueryParserResponseParser.Parse(json, "cmu");

        Assert.True(result.Success);
        Assert.Equal(4, result.TechnicalSpecs.Specs.Count);
        Assert.Equal("8 in", result.TechnicalSpecs.Specs["width"]);
        Assert.Equal("2.5 in", result.TechnicalSpecs.Specs["thickness"]);
    }

    [Fact]
    public void Parse_NonDimensionSpecs_ExtractsCorrectly()
    {
        var json = @"{""material_family"":""window"",""technical_specs"":{""width"":""36 in"",""height"":""48 in"",""u_factor"":""0.30"",""shgc"":""0.25""},""search_query"":""window"",""confidence"":0.90}";

        var result = QueryParserResponseParser.Parse(json, "vinyl window");

        Assert.True(result.Success);
        Assert.Equal("0.30", result.TechnicalSpecs.Specs["u_factor"]);
        Assert.Equal("0.25", result.TechnicalSpecs.Specs["shgc"]);
        Assert.Equal("36 in", result.TechnicalSpecs.Specs["width"]);
    }

    [Fact]
    public void Parse_EmptyTechnicalSpecs_CreatesEmptyDictionary()
    {
        var json = @"{""material_family"":""lumber"",""technical_specs"":{},""search_query"":""lumber"",""confidence"":0.85}";

        var result = QueryParserResponseParser.Parse(json, "lumber");

        Assert.True(result.Success);
        Assert.Empty(result.TechnicalSpecs.Specs);
    }

    [Fact]
    public void Parse_MissingTechnicalSpecs_CreatesEmptyDictionary()
    {
        var json = @"{""material_family"":""stucco"",""search_query"":""stucco EIFS"",""confidence"":0.8}";

        var result = QueryParserResponseParser.Parse(json, "stucco");

        Assert.True(result.Success);
        Assert.Empty(result.TechnicalSpecs.Specs);
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
