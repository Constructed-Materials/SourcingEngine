using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using SourcingEngine.Common.Models;
using SourcingEngine.Core.Services;
using Xunit;

namespace SourcingEngine.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="QueryEmbeddingTextBuilder"/>.
/// Validates the unified 5-section [PRODUCT] [DESCRIPTION] [TECHNICALSPECS] [CERTIFICATIONS] [PRODUCTENRICHMENT]
/// output format, enriched description merging, and that all labels are always present.
/// </summary>
public class QueryEmbeddingTextBuilderTests
{
    private readonly Mock<IEmbeddingTextEnricher> _enricherMock;
    private readonly QueryEmbeddingTextBuilder _builder;

    public QueryEmbeddingTextBuilderTests()
    {
        _enricherMock = new Mock<IEmbeddingTextEnricher>();
        var logger = new Mock<ILogger<QueryEmbeddingTextBuilder>>();

        // Default: enricher returns deterministic text
        _enricherMock.Setup(e => e.EnrichBomItemTextAsync(
                It.IsAny<BomLineItem>(), It.IsAny<ParsedBomQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BomLineItem item, ParsedBomQuery pq, CancellationToken _) =>
                new EnrichedEmbeddingText
                {
                    Description = item.Description ?? "",
                    TechnicalSpecs = new List<TechnicalSpecItem>(),
                    Enrichment = ""
                });

        _builder = new QueryEmbeddingTextBuilder(_enricherMock.Object, logger.Object);
    }

    // ──────────────────────────────────────────────────────
    // 5-section format tests
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task BuildQueryEmbeddingTextAsync_AllFieldsPopulated_ContainsAll5Sections()
    {
        var item = new BomLineItem
        {
            BomItem = "Masonry Block",
            Description = "8 inch masonry block",
            TechnicalSpecs = new List<TechnicalSpecItem>
            {
                new() { Name = "width", Value = 8, Uom = "in" },
                new() { Name = "height", Value = 8, Uom = "in" }
            },
            Certifications = new List<string> { "ASTM C90", "CSA A165.1" }
        };
        var parsed = new ParsedBomQuery
        {
            MaterialFamily = "cmu_blocks",
            TechnicalSpecs = new TechnicalSpecs { Specs = new() { ["width"] = "8 in", ["height"] = "8 in" } },
            Attributes = new() { ["color"] = "gray", ["type"] = "standard" },
            SearchQuery = "8 inch 200 mm 20 cm 8x8x16 concrete masonry unit CMU concrete block masonry block gray",
            Success = true
        };

        _enricherMock.Setup(e => e.EnrichBomItemTextAsync(
                It.IsAny<BomLineItem>(), It.IsAny<ParsedBomQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnrichedEmbeddingText
            {
                Description = "8 inch standard weight concrete masonry block for load-bearing wall construction",
                TechnicalSpecs = new List<TechnicalSpecItem>(),   // enricher never provides specs for BOM items
                Enrichment = "Category: Masonry. Family: cmu blocks. Color: gray, type: standard."
            });

        var result = await _builder.BuildQueryEmbeddingTextAsync(item, parsed);

        Assert.Contains("[PRODUCT] Masonry Block", result);
        Assert.Contains("[DESCRIPTION] 8 inch standard weight concrete masonry block", result);
        // Specs come directly from item.TechnicalSpecs, not the enricher
        Assert.Contains("\"name\":\"width\"", result);
        Assert.Contains("\"name\":\"height\"", result);
        Assert.Contains("[CERTIFICATIONS] ASTM C90, CSA A165.1", result);
        Assert.Contains("[PRODUCTENRICHMENT]", result);
    }

    [Fact]
    public async Task BuildQueryEmbeddingTextAsync_AllLabelsAlwaysPresent_EvenWhenEmpty()
    {
        var item = new BomLineItem { BomItem = "Unknown Item", Description = "" };
        var parsed = new ParsedBomQuery
        {
            MaterialFamily = null,
            TechnicalSpecs = new TechnicalSpecs(),
            SearchQuery = "",
            Success = true
        };

        var result = await _builder.BuildQueryEmbeddingTextAsync(item, parsed);

        // All 5 labels must be present, even with empty content
        Assert.Contains("[PRODUCT]", result);
        Assert.Contains("[DESCRIPTION]", result);
        Assert.Contains("[TECHNICALSPECS]", result);
        Assert.Contains("[CERTIFICATIONS]", result);
        Assert.Contains("[PRODUCTENRICHMENT]", result);
    }

    [Fact]
    public async Task BuildQueryEmbeddingTextAsync_EmptySections_UseEmptyPlaceholder()
    {
        var item = new BomLineItem { BomItem = "Block", Description = "" };
        var parsed = new ParsedBomQuery
        {
            MaterialFamily = null,
            TechnicalSpecs = new TechnicalSpecs(),
            SearchQuery = "",
            Success = true
        };

        var result = await _builder.BuildQueryEmbeddingTextAsync(item, parsed);

        // Empty sections should have "[]" placeholder
        Assert.Contains("[CERTIFICATIONS] []", result);
    }

    [Fact]
    public async Task BuildQueryEmbeddingTextAsync_SectionOrder_IsFixed()
    {
        var item = new BomLineItem
        {
            BomItem = "Window",
            Description = "vinyl window",
            Certifications = new List<string> { "ENERGY STAR" },
            TechnicalSpecs = new List<TechnicalSpecItem>
            {
                new() { Name = "width", Value = 36, Uom = "in" }
            }
        };
        var parsed = new ParsedBomQuery
        {
            MaterialFamily = "windows",
            TechnicalSpecs = new TechnicalSpecs { Specs = new() { ["width"] = "36 in" } },
            SearchQuery = "vinyl window fenestration",
            Success = true
        };

        _enricherMock.Setup(e => e.EnrichBomItemTextAsync(
                It.IsAny<BomLineItem>(), It.IsAny<ParsedBomQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnrichedEmbeddingText
            {
                Description = "vinyl window fenestration",
                TechnicalSpecs = new List<TechnicalSpecItem>(),   // enricher never provides specs for BOM items
                Enrichment = "windows family"
            });

        var result = await _builder.BuildQueryEmbeddingTextAsync(item, parsed);

        var productIdx = result.IndexOf("[PRODUCT]");
        var descIdx = result.IndexOf("[DESCRIPTION]");
        var specsIdx = result.IndexOf("[TECHNICALSPECS]");
        var certsIdx = result.IndexOf("[CERTIFICATIONS]");
        var enrichIdx = result.IndexOf("[PRODUCTENRICHMENT]");

        Assert.True(productIdx < descIdx, "PRODUCT should come before DESCRIPTION");
        Assert.True(descIdx < specsIdx, "DESCRIPTION should come before TECHNICALSPECS");
        Assert.True(specsIdx < certsIdx, "TECHNICALSPECS should come before CERTIFICATIONS");
        Assert.True(certsIdx < enrichIdx, "CERTIFICATIONS should come before PRODUCTENRICHMENT");
    }

    [Fact]
    public async Task BuildQueryEmbeddingTextAsync_UsesBomItemTechnicalSpecs_WhenPresent()
    {
        var item = new BomLineItem
        {
            BomItem = "Granite Cladding",
            Description = "Silver Grey Granite 30mm",
            TechnicalSpecs = new List<TechnicalSpecItem>
            {
                new() { Name = "thickness", Value = 30, Uom = "mm" },
                new() { Name = "weight_per_area", Value = 18.5, Uom = "lbs/sq ft" }
            }
        };
        var parsed = new ParsedBomQuery
        {
            TechnicalSpecs = new TechnicalSpecs { Specs = new() { ["width"] = "30 mm" } },
            SearchQuery = "granite cladding",
            Success = true
        };

        // Enricher only provides description + enrichment (no specs for BOM items)
        _enricherMock.Setup(e => e.EnrichBomItemTextAsync(
                It.IsAny<BomLineItem>(), It.IsAny<ParsedBomQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnrichedEmbeddingText
            {
                Description = "Silver grey granite cladding, 30mm thickness",
                TechnicalSpecs = new List<TechnicalSpecItem>(),
                Enrichment = ""
            });

        var result = await _builder.BuildQueryEmbeddingTextAsync(item, parsed);

        // Specs come directly from item.TechnicalSpecs
        Assert.Contains("\"name\":\"thickness\"", result);
        Assert.Contains("\"value\":30", result);
        Assert.Contains("\"name\":\"weight_per_area\"", result);
        Assert.Contains("18.5", result);
    }

    [Fact]
    public async Task BuildQueryEmbeddingTextAsync_NoBomSpecs_EmitsEmptyPlaceholder()
    {
        // BOM item has no TechnicalSpecs → [TECHNICALSPECS] should be empty placeholder
        var item = new BomLineItem { BomItem = "Block", Description = "masonry block" };
        var parsed = new ParsedBomQuery
        {
            TechnicalSpecs = new TechnicalSpecs { Specs = new() { ["width"] = "8 in", ["height"] = "8 in" } },
            SearchQuery = "masonry block",
            Success = true
        };

        var result = await _builder.BuildQueryEmbeddingTextAsync(item, parsed);

        Assert.Contains("[TECHNICALSPECS] []", result);
    }

    [Fact]
    public async Task BuildQueryEmbeddingTextAsync_NoCertifications_EmitsEmptyPlaceholder()
    {
        var item = new BomLineItem { BomItem = "Block", Description = "masonry block" };
        var parsed = new ParsedBomQuery
        {
            TechnicalSpecs = new TechnicalSpecs(),
            SearchQuery = "masonry block",
            Success = true
        };

        var result = await _builder.BuildQueryEmbeddingTextAsync(item, parsed);

        Assert.Contains("[CERTIFICATIONS] []", result);
    }

    // ──────────────────────────────────────────────────────
    // BuildEnrichedDescription unit tests (static, unchanged)
    // ──────────────────────────────────────────────────────

    [Fact]
    public void BuildEnrichedDescription_MergesAndDeduplicates()
    {
        var result = QueryEmbeddingTextBuilder.BuildEnrichedDescription(
            "8 inch masonry block",
            "8 inch 200 mm 20 cm masonry block CMU concrete block");

        Assert.StartsWith("8 inch masonry block", result);
        Assert.Contains("200", result);
        Assert.Contains("mm", result);
        Assert.Contains("CMU", result);
        Assert.Contains("concrete", result);
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

        Assert.Equal("8 inch masonry block gray", result);
    }

    [Fact]
    public void BuildEnrichedDescription_PreservesOriginalOrder()
    {
        var result = QueryEmbeddingTextBuilder.BuildEnrichedDescription(
            "vinyl window",
            "vinyl window fenestration 900 mm 1200 mm double hung");

        Assert.StartsWith("vinyl window", result);
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
