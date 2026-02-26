using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SourcingEngine.Core.Configuration;
using SourcingEngine.Core.Repositories;
using SourcingEngine.Core.Services;
using Xunit;

namespace SourcingEngine.Tests.Unit;

/// <summary>
/// Unit tests for SpecMatchReRanker — verifies that spec-based re-ranking
/// correctly promotes dimensionally-matching products across all families.
/// </summary>
public class SpecMatchReRankerTests
{
    private readonly SpecMatchReRanker _sut;

    public SpecMatchReRankerTests()
    {
        var settings = Options.Create(new SemanticSearchSettings
        {
            EnableSpecReRanking = true,
            SemanticWeight = 0.6f,
            SpecMatchWeight = 0.4f
        });
        var logger = new Mock<ILogger<SpecMatchReRanker>>();
        _sut = new SpecMatchReRanker(settings, logger.Object);
    }

    // ── IsKeyMatch ─────────────────────────────────────────────────

    [Theory]
    [InlineData("width", "width_mm", true)]
    [InlineData("width", "width_in", true)]
    [InlineData("width", "width_mm_options", true)]
    [InlineData("width", "available_widths_mm", true)]
    [InlineData("height", "height_mm", true)]
    [InlineData("height", "height_inches", true)]
    [InlineData("width", "height_mm", false)]
    [InlineData("width", "u_factor", false)]
    [InlineData("thickness", "thickness_mm", true)]
    [InlineData("depth", "frame_depth_mm", false)] // "frame_depth" base ≠ "depth"
    public void IsKeyMatch_MatchesCorrectly(string queryKey, string productKey, bool expected)
    {
        Assert.Equal(expected, SpecMatchReRanker.IsKeyMatch(queryKey, productKey));
    }

    // ── ReRank — disabled / no specs ───────────────────────────────

    [Fact]
    public void ReRank_Disabled_ReturnsOriginalOrder()
    {
        var settings = Options.Create(new SemanticSearchSettings
        {
            EnableSpecReRanking = false
        });
        var sut = new SpecMatchReRanker(settings, new Mock<ILogger<SpecMatchReRanker>>().Object);

        var matches = MakeMatches();
        var result = sut.ReRank(matches, new TechnicalSpecs { Specs = { ["width"] = "8 in" } });

        Assert.Same(matches, result); // returns same reference
    }

    [Fact]
    public void ReRank_NullSpecs_ReturnsOriginalOrder()
    {
        var matches = MakeMatches();
        var result = _sut.ReRank(matches, null);
        Assert.Same(matches, result);
    }

    [Fact]
    public void ReRank_EmptySpecs_ReturnsOriginalOrder()
    {
        var matches = MakeMatches();
        var result = _sut.ReRank(matches, new TechnicalSpecs());
        Assert.Same(matches, result);
    }

    // ── ReRank — CMU 8-inch scenario (the original bug) ────────────

    [Fact]
    public void ReRank_CmuBlocks_8Inch_PromotesCorrectWidth()
    {
        // Reproduce the original bug: product with 140mm (wrong) scored higher
        // than product with 200mm (~8 in ≈ 203mm, closest)
        var wrongProduct = MakeMatch("Wrong", "Unit 2", 0.627f,
            """{"height_mm": 190, "length_mm": 390, "width_mm_options": [100, 140]}""");
        var correctProduct = MakeMatch("Correct", "Unit 13", 0.611f,
            """{"height_mm": 190, "length_mm": 390, "width_mm_options": [190, 240, 290]}""");

        var matches = new List<SemanticProductMatch> { wrongProduct, correctProduct };
        var querySpecs = new TechnicalSpecs { Specs = { ["width"] = "8 in" } };

        var result = _sut.ReRank(matches, querySpecs);

        // After re-ranking, the product with 190mm should rank higher
        // because 190mm is closer to 203.2mm (8 in) than 140mm
        Assert.Equal("Unit 13", result[0].ModelName);
        Assert.Equal("Unit 2", result[1].ModelName);

        // Both should have FinalScore set
        Assert.NotNull(result[0].FinalScore);
        Assert.NotNull(result[1].FinalScore);
        Assert.True(result[0].FinalScore > result[1].FinalScore);
    }

    // ── ReRank — window U-factor (non-dimensional) ─────────────────

    [Fact]
    public void ReRank_Window_CategoricalMatch()
    {
        var exact = MakeMatch("V1", "Window-A", 0.7f,
            """{"u_factor": "0.30", "shgc": "0.25"}""");
        var noMatch = MakeMatch("V2", "Window-B", 0.75f,
            """{"u_factor": "0.45", "shgc": "0.35"}""");

        var matches = new List<SemanticProductMatch> { noMatch, exact };
        var querySpecs = new TechnicalSpecs
        {
            Specs = { ["u_factor"] = "0.30" }
        };

        var result = _sut.ReRank(matches, querySpecs);

        // Window-A should be promoted because u_factor matches exactly
        Assert.Equal("Window-A", result[0].ModelName);
    }

    // ── ReRank — multi-spec scoring ────────────────────────────────

    [Fact]
    public void ReRank_MultiSpec_AveragesScores()
    {
        // Product that matches width but not height should score lower
        // than one that matches both
        var bothMatch = MakeMatch("V1", "Perfect", 0.6f,
            """{"width_mm": 200, "height_mm": 190}""");
        var widthOnly = MakeMatch("V2", "PartialMatch", 0.65f,
            """{"width_mm": 200, "height_mm": 400}""");

        var matches = new List<SemanticProductMatch> { widthOnly, bothMatch };
        var querySpecs = new TechnicalSpecs
        {
            Specs =
            {
                ["width"] = "200 mm",
                ["height"] = "190 mm"
            }
        };

        var result = _sut.ReRank(matches, querySpecs);

        // "Perfect" should rank higher (matches both specs)
        Assert.Equal("Perfect", result[0].ModelName);
    }

    // ── ReRank — product with no specs JSON ────────────────────────

    [Fact]
    public void ReRank_ProductWithNoSpecs_GetsZeroSpecScore()
    {
        var withSpecs = MakeMatch("V1", "HasSpecs", 0.6f,
            """{"width_mm": 200}""");
        var noSpecs = MakeMatch("V2", "NoSpecs", 0.7f, null);

        var matches = new List<SemanticProductMatch> { noSpecs, withSpecs };
        var querySpecs = new TechnicalSpecs { Specs = { ["width"] = "200 mm" } };

        var result = _sut.ReRank(matches, querySpecs);

        // HasSpecs should get promoted via spec score even though NoSpecs had higher semantic
        Assert.Equal("HasSpecs", result[0].ModelName);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static List<SemanticProductMatch> MakeMatches() =>
    [
        MakeMatch("V1", "M1", 0.8f, null),
        MakeMatch("V2", "M2", 0.7f, null),
    ];

    private static SemanticProductMatch MakeMatch(
        string vendor, string model, float similarity, string? specsJson) => new()
    {
        ProductId = Guid.NewGuid(),
        VendorName = vendor,
        ModelName = model,
        FamilyLabel = "test",
        CsiCode = null,
        Similarity = similarity,
        SpecificationsJson = specsJson
    };
}
