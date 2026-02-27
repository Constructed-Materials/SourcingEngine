using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SourcingEngine.Common.Models;
using SourcingEngine.Core.Configuration;
using SourcingEngine.Core.Models;
using SourcingEngine.Core.Repositories;
using SourcingEngine.Core.Services;
using Xunit;

namespace SourcingEngine.Tests.Unit;

/// <summary>
/// Comprehensive unit tests for ProductFirstStrategy.
/// Mocks all dependencies so tests run without a database or external services.
/// </summary>
public class ProductFirstStrategyTests
{
    private readonly Mock<ISemanticProductRepository> _semanticRepoMock;
    private readonly Mock<IProductEnrichedRepository> _enrichedRepoMock;
    private readonly Mock<IEmbeddingService> _embeddingMock;
    private readonly Mock<IQueryParserService> _queryParserMock;
    private readonly Mock<IQueryEmbeddingTextBuilder> _queryEmbeddingTextBuilderMock;
    private readonly Mock<ISpecMatchReRanker> _specMatchReRankerMock;
    private readonly Mock<ILogger<ProductFirstStrategy>> _loggerMock;
    private readonly SemanticSearchSettings _settings;

    private ProductFirstStrategy CreateStrategy()
    {
        return new ProductFirstStrategy(
            _semanticRepoMock.Object,
            _enrichedRepoMock.Object,
            _embeddingMock.Object,
            _queryParserMock.Object,
            _queryEmbeddingTextBuilderMock.Object,
            _specMatchReRankerMock.Object,
            Options.Create(_settings),
            _loggerMock.Object);
    }

    public ProductFirstStrategyTests()
    {
        _semanticRepoMock = new Mock<ISemanticProductRepository>();
        _enrichedRepoMock = new Mock<IProductEnrichedRepository>();
        _embeddingMock = new Mock<IEmbeddingService>();
        _queryParserMock = new Mock<IQueryParserService>();
        _queryEmbeddingTextBuilderMock = new Mock<IQueryEmbeddingTextBuilder>();
        _specMatchReRankerMock = new Mock<ISpecMatchReRanker>();
        _loggerMock = new Mock<ILogger<ProductFirstStrategy>>();

        _settings = new SemanticSearchSettings
        {
            Enabled = true,
            SimilarityThreshold = 0.3f,
            MatchCount = 20
        };

        // Default: embedding service returns a valid vector
        _embeddingMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[768]);

        // Default: enriched repo returns empty
        _enrichedRepoMock.Setup(r => r.GetEnrichedDataAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductEnriched>());

        // Default: query parser succeeds with basic result
        _queryParserMock.Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParsedBomQuery
            {
                Success = true,
                SearchQuery = "parsed query",
                Confidence = 0.9f,
                MaterialFamily = "cmu_blocks",
                TechnicalSpecs = new TechnicalSpecs()
            });

        // Default: query embedding text builder returns structured text
        _queryEmbeddingTextBuilderMock
            .Setup(b => b.BuildQueryEmbeddingText(It.IsAny<BomLineItem>(), It.IsAny<ParsedBomQuery>()))
            .Returns<BomLineItem, ParsedBomQuery>((item, _) => $"[DESCRIPTION] {item.Spec}");

        // Default: semantic search returns empty
        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), It.IsAny<SearchFilters?>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticProductMatch>());

        // Default: re-ranker passes through unchanged
        _specMatchReRankerMock.Setup(r => r.ReRank(It.IsAny<List<SemanticProductMatch>>(), It.IsAny<TechnicalSpecs?>()))
            .Returns((List<SemanticProductMatch> matches, TechnicalSpecs? _) => matches);
    }

    private static BomLineItem MakeItem(string spec, string? bomItem = null) => new()
    {
        BomItem = bomItem ?? spec,
        Spec = spec
    };

    private static SemanticProductMatch MakeMatch(
        string vendor, string model, string? family = null,
        string? csi = null, float similarity = 0.8f, Guid? id = null) => new()
    {
        ProductId = id ?? Guid.NewGuid(),
        VendorName = vendor,
        ModelName = model,
        FamilyLabel = family,
        CsiCode = csi,
        Similarity = similarity
    };

    // ── Basic search flow ──────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WithMatches_ReturnsProductMatches()
    {
        var match = MakeMatch("Acme", "Widget-100", "cmu_blocks", "042200", 0.85f);
        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), It.IsAny<SearchFilters?>(), _settings.SimilarityThreshold, _settings.MatchCount, It.IsAny<CancellationToken>()))
            .ReturnsAsync([match]);

        var sut = CreateStrategy();
        var result = await sut.ExecuteAsync(MakeItem("8 inch cmu block"), CancellationToken.None);

        Assert.Single(result.Matches);
        Assert.Equal("Acme", result.Matches[0].Vendor);
        Assert.Equal("Widget-100", result.Matches[0].ModelName);
        Assert.Equal(0.85f, result.Matches[0].SemanticScore);
    }

    [Fact]
    public async Task ExecuteAsync_NoMatches_ReturnsEmptyList()
    {
        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticProductMatch>());

        var sut = CreateStrategy();
        var result = await sut.ExecuteAsync(MakeItem("nonexistent product"), CancellationToken.None);

        Assert.Empty(result.Matches);
    }

    // ── LLM query parsing ──────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_UsesQueryEmbeddingTextBuilder_WhenParserSucceeds()
    {
        _queryParserMock.Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParsedBomQuery
            {
                Success = true,
                SearchQuery = "concrete masonry unit block 8 inch load bearing",
                MaterialFamily = "cmu_blocks",
                Confidence = 0.9f,
                TechnicalSpecs = new TechnicalSpecs()
            });

        _queryEmbeddingTextBuilderMock
            .Setup(b => b.BuildQueryEmbeddingText(It.IsAny<BomLineItem>(), It.IsAny<ParsedBomQuery>()))
            .Returns("[FAMILY] cmu_blocks\n[DESCRIPTION] 8 inch cmu");

        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticProductMatch>());

        var sut = CreateStrategy();
        await sut.ExecuteAsync(MakeItem("8 inch cmu"), CancellationToken.None);

        // The structured embedding text should be passed to the embedding service
        _embeddingMock.Verify(e => e.GenerateEmbeddingAsync(
            "[FAMILY] cmu_blocks\n[DESCRIPTION] 8 inch cmu", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_FallsBackToRawText_WhenParserFails()
    {
        _queryParserMock.Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("LLM unavailable"));

        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticProductMatch>());

        var sut = CreateStrategy();
        var result = await sut.ExecuteAsync(MakeItem("cmu block"), CancellationToken.None);

        // Should fall back to the original spec text
        _embeddingMock.Verify(e => e.GenerateEmbeddingAsync(
            "cmu block", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains(result.Warnings, w => w.Contains("LLM parsing failed"));
    }

    [Fact]
    public async Task ExecuteAsync_FallsBackToRawText_WhenParserReturnsFailure()
    {
        _queryParserMock.Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParsedBomQuery
            {
                Success = false,
                ErrorMessage = "Could not parse",
                SearchQuery = "",
                TechnicalSpecs = new TechnicalSpecs()
            });

        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticProductMatch>());

        var sut = CreateStrategy();
        var result = await sut.ExecuteAsync(MakeItem("stucco eifs"), CancellationToken.None);

        _embeddingMock.Verify(e => e.GenerateEmbeddingAsync(
            "stucco eifs", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains(result.Warnings, w => w.Contains("LLM parsing failed"));
    }

    [Fact]
    public async Task ExecuteAsync_UsesBomItem_WhenSpecIsEmpty()
    {
        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticProductMatch>());

        var sut = CreateStrategy();
        var item = new BomLineItem { BomItem = "floor joist", Spec = "" };
        await sut.ExecuteAsync(item, CancellationToken.None);

        // When Spec is empty, should use BomItem as input
        _queryParserMock.Verify(p => p.ParseAsync("floor joist", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Family label derivation ────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_DerivesFamilyLabel_FromMostCommonMatch()
    {
        var matches = new List<SemanticProductMatch>
        {
            MakeMatch("V1", "M1", "cmu_blocks"),
            MakeMatch("V2", "M2", "cmu_blocks"),
            MakeMatch("V3", "M3", "masonry"),
        };
        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), It.IsAny<SearchFilters?>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matches);

        var sut = CreateStrategy();
        var result = await sut.ExecuteAsync(MakeItem("block"), CancellationToken.None);

        Assert.Equal("cmu_blocks", result.FamilyLabel);
    }

    [Fact]
    public async Task ExecuteAsync_NullFamilyLabel_WhenNoMatchesHaveFamily()
    {
        var matches = new List<SemanticProductMatch>
        {
            MakeMatch("V1", "M1", family: null),
            MakeMatch("V2", "M2", family: ""),
        };
        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), It.IsAny<SearchFilters?>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matches);

        var sut = CreateStrategy();
        var result = await sut.ExecuteAsync(MakeItem("query"), CancellationToken.None);

        Assert.Null(result.FamilyLabel);
    }

    // ── CSI code derivation ────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_DerivesCsiCode_FromFirstNonNullMatch()
    {
        var matches = new List<SemanticProductMatch>
        {
            MakeMatch("V1", "M1", csi: null),
            MakeMatch("V2", "M2", csi: "042200"),
            MakeMatch("V3", "M3", csi: "099999"),
        };
        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), It.IsAny<SearchFilters?>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matches);

        var sut = CreateStrategy();
        var result = await sut.ExecuteAsync(MakeItem("block"), CancellationToken.None);

        Assert.Equal("042200", result.CsiCode);
    }

    // ── Enrichment integration ─────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_EnrichesMatches_WithVendorData()
    {
        var productId = Guid.NewGuid();
        var matches = new List<SemanticProductMatch>
        {
            MakeMatch("Kawneer", "1600UT", id: productId)
        };
        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), It.IsAny<SearchFilters?>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matches);

        _enrichedRepoMock.Setup(r => r.GetEnrichedDataAsync(
                It.Is<List<Guid>>(ids => ids.Contains(productId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ProductEnriched
                {
                    ProductId = productId,
                    UseWhen = "Commercial storefront applications",
                    ModelCode = "1600UT-STD",
                    KeyFeaturesJson = "[\"Thermal break\",\"Hurricane rated\"]",
                    SourceSchema = "kawneer"
                }
            ]);

        var sut = CreateStrategy();
        var result = await sut.ExecuteAsync(MakeItem("curtain wall"), CancellationToken.None);

        var match = Assert.Single(result.Matches);
        Assert.Equal("Commercial storefront applications", match.UseWhen);
        Assert.Equal("1600UT-STD", match.ModelCode);
        Assert.Equal("kawneer", match.SourceSchema);
        Assert.NotNull(match.KeyFeatures);
        Assert.Contains("Thermal break", match.KeyFeatures);
    }

    [Fact]
    public async Task ExecuteAsync_HandlesPartialEnrichment_Gracefully()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        var matches = new List<SemanticProductMatch>
        {
            MakeMatch("V1", "M1", id: id1),
            MakeMatch("V2", "M2", id: id2),
            MakeMatch("V3", "M3", id: id3),
        };
        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), It.IsAny<SearchFilters?>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matches);

        _enrichedRepoMock.Setup(r => r.GetEnrichedDataAsync(
                It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ProductEnriched { ProductId = id2, UseWhen = "Indoor use", SourceSchema = "vendor2" }
            ]);

        var sut = CreateStrategy();
        var result = await sut.ExecuteAsync(MakeItem("query"), CancellationToken.None);

        Assert.Equal(3, result.Matches.Count);
        Assert.Null(result.Matches[0].UseWhen);
        Assert.Equal("Indoor use", result.Matches[1].UseWhen);
        Assert.Null(result.Matches[2].UseWhen);
    }

    // ── Error handling ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_EmbeddingFailure_Throws()
    {
        _embeddingMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Service unavailable"));

        var sut = CreateStrategy();
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            sut.ExecuteAsync(MakeItem("test"), CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_SemanticRepoFailure_Throws()
    {
        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), It.IsAny<SearchFilters?>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB connection lost"));

        var sut = CreateStrategy();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ExecuteAsync(MakeItem("test"), CancellationToken.None));
    }

    // ── Similarity scores ──────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_PreservesSemanticScores()
    {
        var matches = new List<SemanticProductMatch>
        {
            MakeMatch("V1", "TopMatch", similarity: 0.95f),
            MakeMatch("V2", "MidMatch", similarity: 0.72f),
            MakeMatch("V3", "LowMatch", similarity: 0.51f),
        };
        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), It.IsAny<SearchFilters?>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matches);

        var sut = CreateStrategy();
        var result = await sut.ExecuteAsync(MakeItem("block"), CancellationToken.None);

        Assert.Equal(0.95f, result.Matches[0].SemanticScore);
        Assert.Equal(0.72f, result.Matches[1].SemanticScore);
        Assert.Equal(0.51f, result.Matches[2].SemanticScore);
    }

    // ── Settings-driven behaviour ──────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_RespectsMatchCount_Setting()
    {
        _settings.MatchCount = 5;

        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), It.IsAny<SearchFilters?>(), 0.3f, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticProductMatch>());

        var sut = CreateStrategy();
        await sut.ExecuteAsync(MakeItem("query"), CancellationToken.None);

        _semanticRepoMock.Verify(r => r.SearchByEmbeddingAsync(
            It.IsAny<float[]>(), It.IsAny<SearchFilters?>(), 0.3f, 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RespectsSimilarityThreshold_Setting()
    {
        _settings.SimilarityThreshold = 0.7f;

        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), It.IsAny<SearchFilters?>(), 0.7f, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticProductMatch>());

        var sut = CreateStrategy();
        await sut.ExecuteAsync(MakeItem("query"), CancellationToken.None);

        _semanticRepoMock.Verify(r => r.SearchByEmbeddingAsync(
            It.IsAny<float[]>(), It.IsAny<SearchFilters?>(), 0.7f, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Cancellation ───────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_HonoursCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _embeddingMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var sut = CreateStrategy();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.ExecuteAsync(MakeItem("query"), cts.Token));
    }

    // ── JSON parsing edge cases (via enrichment) ───────────────────

    [Fact]
    public async Task ExecuteAsync_HandlesInvalidJsonInEnrichedData()
    {
        var productId = Guid.NewGuid();
        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), It.IsAny<SearchFilters?>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeMatch("V1", "M1", id: productId)]);

        _enrichedRepoMock.Setup(r => r.GetEnrichedDataAsync(
                It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ProductEnriched
                {
                    ProductId = productId,
                    KeyFeaturesJson = "not valid json",
                    TechnicalSpecsJson = "{broken",
                    SourceSchema = "test"
                }
            ]);

        var sut = CreateStrategy();
        var result = await sut.ExecuteAsync(MakeItem("test"), CancellationToken.None);

        var match = Assert.Single(result.Matches);
        Assert.Null(match.KeyFeatures);
        Assert.Null(match.TechnicalSpecs);
    }

    // ── Post-rerank threshold filter (Step 6b) ─────────────────────

    [Fact]
    public async Task ExecuteAsync_PostRerankFilter_RemovesMatchesBelowThreshold()
    {
        // Arrange: threshold = 0.5; re-ranker returns one above and one below
        _settings.SimilarityThreshold = 0.5f;

        var aboveId = Guid.NewGuid();
        var belowId = Guid.NewGuid();
        var semanticMatches = new List<SemanticProductMatch>
        {
            MakeMatch("V1", "Good", id: aboveId) with { FinalScore = 0.75f },
            MakeMatch("V2", "Weak", id: belowId) with { FinalScore = 0.35f }
        };

        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), It.IsAny<SearchFilters?>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(semanticMatches);

        // Re-ranker passes through (already has FinalScore set)
        _specMatchReRankerMock.Setup(r => r.ReRank(It.IsAny<List<SemanticProductMatch>>(), It.IsAny<TechnicalSpecs?>()))
            .Returns((List<SemanticProductMatch> m, TechnicalSpecs? _) => m);

        var sut = CreateStrategy();
        var result = await sut.ExecuteAsync(MakeItem("test item"), CancellationToken.None);

        // Only the match above threshold should remain
        Assert.Single(result.Matches);
        Assert.Equal("Good", result.Matches[0].ModelName);
    }

    [Fact]
    public async Task ExecuteAsync_PostRerankFilter_UsesSimilarityWhenFinalScoreNull()
    {
        _settings.SimilarityThreshold = 0.5f;

        var id = Guid.NewGuid();
        var semanticMatches = new List<SemanticProductMatch>
        {
            MakeMatch("V1", "NoFinalScore", similarity: 0.7f, id: id)
            // FinalScore is null by default
        };

        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), It.IsAny<SearchFilters?>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(semanticMatches);

        _specMatchReRankerMock.Setup(r => r.ReRank(It.IsAny<List<SemanticProductMatch>>(), It.IsAny<TechnicalSpecs?>()))
            .Returns((List<SemanticProductMatch> m, TechnicalSpecs? _) => m);

        var sut = CreateStrategy();
        var result = await sut.ExecuteAsync(MakeItem("test item"), CancellationToken.None);

        // Similarity 0.7 > threshold 0.5, should keep it
        Assert.Single(result.Matches);
    }
}
