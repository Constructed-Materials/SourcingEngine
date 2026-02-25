using Microsoft.Extensions.Logging;
using Moq;
using SourcingEngine.Common.Models;
using SourcingEngine.Core.Services;
using Xunit;

namespace SourcingEngine.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="QueryEmbeddingTextBuilder"/>.
/// Validates structured [SECTION] output, enriched description merging,
/// and deduplication of overlapping tokens between spec and SearchQuery.
/// </summary>
public class QueryEmbeddingTextBuilderTests
{
    private readonly QueryEmbeddingTextBuilder _builder;

    public QueryEmbeddingTextBuilderTests()
    {
        var logger = new Mock<ILogger<QueryEmbeddingTextBuilder>>();
        _builder = new QueryEmbeddingTextBuilder(logger.Object);
    }

    // ──────────────────────────────────────────────────────
    // Full pipeline tests
    // ──────────────────────────────────────────────────────

    [Fact]
    public void BuildQueryEmbeddingText_AllFieldsPopulated_ContainsAllSections()
    {
        var item = new BomLineItem { BomItem = "Masonry Block", Spec = "8 inch masonry block" };
        var parsed = new ParsedBomQuery
        {
            MaterialFamily = "cmu_blocks",
            TechnicalSpecs = new TechnicalSpecs { Specs = new() { ["width"] = "8 in", ["height"] = "8 in" } },
            Attributes = new() { ["color"] = "gray", ["type"] = "standard" },
            SearchQuery = "8 inch 200 mm 20 cm 8x8x16 concrete masonry unit CMU concrete block masonry block gray",
            Success = true
        };

        var result = _builder.BuildQueryEmbeddingText(item, parsed);

        Assert.Contains("[FAMILY] cmu blocks (cmu_blocks)", result);
        Assert.Contains("[TECHNICALSPECS] width: 8 in | height: 8 in", result);
        Assert.Contains("[DESCRIPTION]", result);
        Assert.Contains("[USE] color: gray, type: standard", result);
    }

    [Fact]
    public void BuildQueryEmbeddingText_SearchQueryEnrichesDescription()
    {
        var item = new BomLineItem { BomItem = "Masonry Block", Spec = "8 inch masonry block" };
        var parsed = new ParsedBomQuery
        {
            MaterialFamily = "cmu_blocks",
            SearchQuery = "8 inch 200 mm 20 cm CMU concrete masonry unit concrete block masonry block",
            Success = true
        };

        var result = _builder.BuildQueryEmbeddingText(item, parsed);

        // The enriched description should contain the original spec terms
        Assert.Contains("8 inch masonry block", result);
        // AND the additional expanded terms from SearchQuery (deduplicated)
        Assert.Contains("200", result);
        Assert.Contains("mm", result);
        Assert.Contains("CMU", result);
        Assert.Contains("concrete", result);
    }

    [Fact]
    public void BuildQueryEmbeddingText_EmptySearchQuery_UsesSpecOnly()
    {
        var item = new BomLineItem { BomItem = "Block", Spec = "8 inch masonry block" };
        var parsed = new ParsedBomQuery
        {
            MaterialFamily = "cmu_blocks",
            SearchQuery = "",
            Success = true
        };

        var result = _builder.BuildQueryEmbeddingText(item, parsed);

        Assert.Contains("[DESCRIPTION] 8 inch masonry block", result);
        // Should NOT contain synonym-expanded terms
        Assert.DoesNotContain("CMU", result);
        Assert.DoesNotContain("200 mm", result);
    }

    [Fact]
    public void BuildQueryEmbeddingText_NoFamily_OmitsFamilySection()
    {
        var item = new BomLineItem { BomItem = "Unknown", Spec = "some material" };
        var parsed = new ParsedBomQuery
        {
            MaterialFamily = null,
            SearchQuery = "some material building product",
            Success = true
        };

        var result = _builder.BuildQueryEmbeddingText(item, parsed);

        Assert.DoesNotContain("[FAMILY]", result);
        Assert.Contains("[DESCRIPTION]", result);
    }

    [Fact]
    public void BuildQueryEmbeddingText_NoSpecs_OmitsTechnicalSpecsSection()
    {
        var item = new BomLineItem { BomItem = "Block", Spec = "masonry block" };
        var parsed = new ParsedBomQuery
        {
            MaterialFamily = "cmu_blocks",
            TechnicalSpecs = new TechnicalSpecs(),
            SearchQuery = "masonry block CMU",
            Success = true
        };

        var result = _builder.BuildQueryEmbeddingText(item, parsed);

        Assert.DoesNotContain("[TECHNICALSPECS]", result);
    }

    [Fact]
    public void BuildQueryEmbeddingText_NoAttributes_OmitsUseSection()
    {
        var item = new BomLineItem { BomItem = "Block", Spec = "masonry block" };
        var parsed = new ParsedBomQuery
        {
            MaterialFamily = "cmu_blocks",
            Attributes = new(),
            SearchQuery = "masonry block",
            Success = true
        };

        var result = _builder.BuildQueryEmbeddingText(item, parsed);

        Assert.DoesNotContain("[USE]", result);
    }

    [Fact]
    public void BuildQueryEmbeddingText_WindowExample_ProducesCorrectOutput()
    {
        var item = new BomLineItem { BomItem = "Window", Spec = "36x48 vinyl double hung window low-e" };
        var parsed = new ParsedBomQuery
        {
            MaterialFamily = "windows",
            TechnicalSpecs = new TechnicalSpecs
            {
                Specs = new() { ["width"] = "36 in", ["height"] = "48 in" }
            },
            Attributes = new() { ["type"] = "double hung", ["material"] = "vinyl", ["glazing"] = "low-e" },
            SearchQuery = "36x48 vinyl window double hung low-e fenestration 36 inch 48 inch 900 mm 1200 mm",
            Success = true
        };

        var result = _builder.BuildQueryEmbeddingText(item, parsed);

        Assert.Contains("[FAMILY] windows (windows)", result);
        Assert.Contains("[TECHNICALSPECS] width: 36 in | height: 48 in", result);
        Assert.Contains("fenestration", result);
        Assert.Contains("900", result);
        Assert.Contains("1200", result);
        Assert.Contains("[USE]", result);
    }

    // ──────────────────────────────────────────────────────
    // BuildEnrichedDescription unit tests
    // ──────────────────────────────────────────────────────

    [Fact]
    public void BuildEnrichedDescription_MergesAndDeduplicates()
    {
        var result = QueryEmbeddingTextBuilder.BuildEnrichedDescription(
            "8 inch masonry block",
            "8 inch 200 mm 20 cm masonry block CMU concrete block");

        // Original tokens preserved
        Assert.StartsWith("8 inch masonry block", result);
        // New tokens appended
        Assert.Contains("200", result);
        Assert.Contains("mm", result);
        Assert.Contains("CMU", result);
        Assert.Contains("concrete", result);
        // Duplicates NOT repeated (case-insensitive)
        Assert.Equal(1, CountOccurrences(result, "block"));
    }

    [Fact]
    public void BuildEnrichedDescription_IdenticalTexts_ReturnsSpec()
    {
        var result = QueryEmbeddingTextBuilder.BuildEnrichedDescription(
            "8 inch masonry block",
            "8 inch masonry block");

        Assert.Equal("8 inch masonry block", result);
    }

    [Fact]
    public void BuildEnrichedDescription_NullSearchQuery_ReturnsSpec()
    {
        var result = QueryEmbeddingTextBuilder.BuildEnrichedDescription(
            "8 inch masonry block", null);

        Assert.Equal("8 inch masonry block", result);
    }

    [Fact]
    public void BuildEnrichedDescription_EmptySearchQuery_ReturnsSpec()
    {
        var result = QueryEmbeddingTextBuilder.BuildEnrichedDescription(
            "8 inch masonry block", "");

        Assert.Equal("8 inch masonry block", result);
    }

    [Fact]
    public void BuildEnrichedDescription_NullSpec_ReturnsQueryTokens()
    {
        var result = QueryEmbeddingTextBuilder.BuildEnrichedDescription(
            null, "CMU concrete block");

        Assert.Contains("CMU", result);
        Assert.Contains("concrete", result);
        Assert.Contains("block", result);
    }

    [Fact]
    public void BuildEnrichedDescription_CaseInsensitiveDedup()
    {
        var result = QueryEmbeddingTextBuilder.BuildEnrichedDescription(
            "Masonry Block",
            "masonry block CMU");

        // "masonry" and "block" from SearchQuery are case-insensitive duplicates
        Assert.Contains("CMU", result);
        Assert.Equal(1, CountOccurrences(result.ToLower(), "masonry"));
        Assert.Equal(1, CountOccurrences(result.ToLower(), "block"));
    }

    [Fact]
    public void BuildEnrichedDescription_SearchQuerySubsetOfSpec_NoExtraTokens()
    {
        var result = QueryEmbeddingTextBuilder.BuildEnrichedDescription(
            "8 inch masonry block gray",
            "8 inch masonry block");

        // SearchQuery is a subset — no extra tokens to add
        Assert.Equal("8 inch masonry block gray", result);
    }

    [Fact]
    public void BuildEnrichedDescription_PreservesOriginalOrder()
    {
        var result = QueryEmbeddingTextBuilder.BuildEnrichedDescription(
            "vinyl window",
            "vinyl window fenestration 900 mm 1200 mm double hung");

        // Original spec is at the front
        Assert.StartsWith("vinyl window", result);
        // Expanded terms follow
        var expandedPart = result.Substring("vinyl window ".Length);
        Assert.Contains("fenestration", expandedPart);
        Assert.Contains("900", expandedPart);
    }

    // ──────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────

    private static int CountOccurrences(string text, string word)
    {
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Count(t => t.Equals(word, StringComparison.OrdinalIgnoreCase));
    }
}
