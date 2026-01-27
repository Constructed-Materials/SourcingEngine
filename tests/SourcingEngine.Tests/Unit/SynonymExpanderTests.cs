using SourcingEngine.Core.Services;
using Xunit;

namespace SourcingEngine.Tests.Unit;

/// <summary>
/// Unit tests for SynonymExpander - no database required
/// </summary>
public class SynonymExpanderTests
{
    private readonly SynonymExpander _expander = new();

    [Theory]
    [InlineData("cmu", "masonry block")]
    [InlineData("cmu", "concrete block")]
    [InlineData("masonry block", "cmu")]
    [InlineData("stucco", "eifs")]
    [InlineData("railing", "handrail")]
    [InlineData("joist", "i-joist")]
    public void GetSynonyms_KnownTerm_ReturnsSynonyms(string input, string expectedSynonym)
    {
        var synonyms = _expander.GetSynonyms(input);
        
        Assert.Contains(expectedSynonym, synonyms, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetSynonyms_UnknownTerm_ReturnsInputOnly()
    {
        var synonyms = _expander.GetSynonyms("unknownterm12345");
        
        Assert.Single(synonyms);
        Assert.Contains("unknownterm12345", synonyms);
    }

    [Fact]
    public void ExtractKeywords_BomText_ExcludesStopWords()
    {
        var keywords = _expander.ExtractKeywords("8 inch masonry block for the building");
        
        Assert.DoesNotContain("for", keywords);
        Assert.DoesNotContain("the", keywords);
        Assert.Contains("masonry", keywords);
        Assert.Contains("block", keywords);
    }

    [Fact]
    public void ExtractKeywords_BomText_ExcludesPureNumbers()
    {
        var keywords = _expander.ExtractKeywords("8 inch masonry block 2900 sf");
        
        // Pure numbers should be excluded (handled by size calculator)
        Assert.DoesNotContain("8", keywords);
        Assert.DoesNotContain("2900", keywords);
    }

    [Fact]
    public void ExpandTerms_MasonryBlock_IncludesCmu()
    {
        var expanded = _expander.ExpandTerms("8 inch masonry block");
        
        Assert.Contains("cmu", expanded, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("concrete block", expanded, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpandTerms_FloorTruss_IncludesBci()
    {
        var expanded = _expander.ExpandTerms("Pre Engineered Wood Floor Trusses");
        
        Assert.Contains("joist", expanded, StringComparer.OrdinalIgnoreCase);
    }
}
