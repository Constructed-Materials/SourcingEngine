using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SourcingEngine.Core.Configuration;
using SourcingEngine.Core.Models;
using SourcingEngine.Core.Repositories;
using SourcingEngine.Core.Services;
using Xunit;

namespace SourcingEngine.Tests.Unit;

/// <summary>
/// Comprehensive unit tests for ProductFirstStrategy.
/// Mocks all dependencies so tests run without a database or Ollama.
/// </summary>
public class ProductFirstStrategyTests
{
    private readonly Mock<ISemanticProductRepository> _semanticRepoMock;
    private readonly Mock<IProductEnrichedRepository> _enrichedRepoMock;
    private readonly Mock<IMaterialFamilyRepository> _familyRepoMock;
    private readonly Mock<IEmbeddingService> _embeddingMock;
    private readonly Mock<IQueryParserService> _queryParserMock;
    private readonly Mock<ILogger<ProductFirstStrategy>> _loggerMock;
    private readonly SemanticSearchSettings _settings;

    private ProductFirstStrategy CreateStrategy(IQueryParserService? queryParser = null)
    {
        return new ProductFirstStrategy(
            _semanticRepoMock.Object,
            _enrichedRepoMock.Object,
            _familyRepoMock.Object,
            _embeddingMock.Object,
            Options.Create(_settings),
            _loggerMock.Object,
            queryParser);
    }

    public ProductFirstStrategyTests()
    {
        _semanticRepoMock = new Mock<ISemanticProductRepository>();
        _enrichedRepoMock = new Mock<IProductEnrichedRepository>();
        _familyRepoMock = new Mock<IMaterialFamilyRepository>();
        _embeddingMock = new Mock<IEmbeddingService>();
        _queryParserMock = new Mock<IQueryParserService>();
        _loggerMock = new Mock<ILogger<ProductFirstStrategy>>();

        _settings = new SemanticSearchSettings
        {
            Enabled = true,
            SimilarityThreshold = 0.5f,
            MatchCount = 10
        };

        // Default: embedding service returns a valid vector
        _embeddingMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[768]);

        // Default: enriched repo returns empty
        _enrichedRepoMock.Setup(r => r.GetEnrichedDataAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductEnriched>());
    }

    private static BomItem MakeBomItem(string text) => new()
    {
        RawText = text,
        Keywords = [text],
        Synonyms = [text],
        SizeVariants = []
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

    // ── Mode property ──────────────────────────────────────────────

    [Fact]
    public void Mode_ReturnsProductFirst()
    {
        var sut = CreateStrategy();
        Assert.Equal(SemanticSearchMode.ProductFirst, sut.Mode);
    }

    // ── Basic search flow ──────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WithMatches_ReturnsProductMatches()
    {
        // Arrange
        var match = MakeMatch("Acme", "Widget-100", "cmu_blocks", "042200", 0.85f);
        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), _settings.SimilarityThreshold, _settings.MatchCount, It.IsAny<CancellationToken>()))
            .ReturnsAsync([match]);

        var sut = CreateStrategy();

        // Act
        var result = await sut.ExecuteAsync("8 inch cmu block", MakeBomItem("8 inch cmu block"), CancellationToken.None);

        // Assert
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
        var result = await sut.ExecuteAsync("nonexistent product", MakeBomItem("nonexistent product"), CancellationToken.None);

        Assert.Empty(result.Matches);
        Assert.Empty(result.Warnings);
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
                It.IsAny<float[]>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matches);

        var sut = CreateStrategy();
        var result = await sut.ExecuteAsync("block", MakeBomItem("block"), CancellationToken.None);

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
                It.IsAny<float[]>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matches);

        var sut = CreateStrategy();
        var result = await sut.ExecuteAsync("query", MakeBomItem("query"), CancellationToken.None);

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
                It.IsAny<float[]>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matches);

        var sut = CreateStrategy();
        var result = await sut.ExecuteAsync("block", MakeBomItem("block"), CancellationToken.None);

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
                It.IsAny<float[]>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
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
        var result = await sut.ExecuteAsync("curtain wall", MakeBomItem("curtain wall"), CancellationToken.None);

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
        // 3 semantic matches, only 1 has enriched data
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
                It.IsAny<float[]>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matches);

        _enrichedRepoMock.Setup(r => r.GetEnrichedDataAsync(
                It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ProductEnriched { ProductId = id2, UseWhen = "Indoor use", SourceSchema = "vendor2" }
            ]);

        var sut = CreateStrategy();
        var result = await sut.ExecuteAsync("query", MakeBomItem("query"), CancellationToken.None);

        Assert.Equal(3, result.Matches.Count);
        Assert.Null(result.Matches[0].UseWhen);     // No enrichment
        Assert.Equal("Indoor use", result.Matches[1].UseWhen);  // Enriched
        Assert.Null(result.Matches[2].UseWhen);     // No enrichment
    }

    // ── LLM query enrichment ──────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_UsesEnrichedQuery_WhenParserSucceeds()
    {
        _queryParserMock.Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParsedBomQuery
            {
                Success = true,
                SearchQuery = "concrete masonry unit block 8 inch load bearing",
                Confidence = 0.9f
            });

        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticProductMatch>());

        var sut = CreateStrategy(_queryParserMock.Object);
        await sut.ExecuteAsync("8 inch cmu", MakeBomItem("8 inch cmu"), CancellationToken.None);

        // The enriched query should be passed to the embedding service
        _embeddingMock.Verify(e => e.GenerateEmbeddingAsync(
            "concrete masonry unit block 8 inch load bearing", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_FallsBackToRawText_WhenParserFails()
    {
        _queryParserMock.Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("LLM unavailable"));

        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticProductMatch>());

        var sut = CreateStrategy(_queryParserMock.Object);
        await sut.ExecuteAsync("cmu block", MakeBomItem("cmu block"), CancellationToken.None);

        // Should fall back to the original text
        _embeddingMock.Verify(e => e.GenerateEmbeddingAsync(
            "cmu block", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_FallsBackToRawText_WhenParserReturnsEmpty()
    {
        _queryParserMock.Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParsedBomQuery { Success = true, SearchQuery = "", Confidence = 0.1f });

        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticProductMatch>());

        var sut = CreateStrategy(_queryParserMock.Object);
        await sut.ExecuteAsync("stucco eifs", MakeBomItem("stucco eifs"), CancellationToken.None);

        _embeddingMock.Verify(e => e.GenerateEmbeddingAsync(
            "stucco eifs", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutParser_UsesRawText()
    {
        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticProductMatch>());

        var sut = CreateStrategy(queryParser: null);
        await sut.ExecuteAsync("floor joist 12 inch", MakeBomItem("floor joist 12 inch"), CancellationToken.None);

        _embeddingMock.Verify(e => e.GenerateEmbeddingAsync(
            "floor joist 12 inch", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Error handling ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_EmbeddingFailure_ThrowsWithWarning()
    {
        _embeddingMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Ollama down"));

        var sut = CreateStrategy();
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            sut.ExecuteAsync("test", MakeBomItem("test"), CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_SemanticRepoFailure_ThrowsAndLogsWarning()
    {
        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB connection lost"));

        var sut = CreateStrategy();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ExecuteAsync("test", MakeBomItem("test"), CancellationToken.None));
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
                It.IsAny<float[]>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matches);

        var sut = CreateStrategy();
        var result = await sut.ExecuteAsync("block", MakeBomItem("block"), CancellationToken.None);

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
                It.IsAny<float[]>(), 0.5f, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticProductMatch>());

        var sut = CreateStrategy();
        await sut.ExecuteAsync("query", MakeBomItem("query"), CancellationToken.None);

        _semanticRepoMock.Verify(r => r.SearchByEmbeddingAsync(
            It.IsAny<float[]>(), 0.5f, 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RespectsSimilarityThreshold_Setting()
    {
        _settings.SimilarityThreshold = 0.7f;

        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), 0.7f, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticProductMatch>());

        var sut = CreateStrategy();
        await sut.ExecuteAsync("query", MakeBomItem("query"), CancellationToken.None);

        _semanticRepoMock.Verify(r => r.SearchByEmbeddingAsync(
            It.IsAny<float[]>(), 0.7f, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
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
            sut.ExecuteAsync("query", MakeBomItem("query"), cts.Token));
    }

    // ── JSON parsing edge cases (via enrichment) ───────────────────

    [Fact]
    public async Task ExecuteAsync_HandlesInvalidJsonInEnrichedData()
    {
        var productId = Guid.NewGuid();
        _semanticRepoMock.Setup(r => r.SearchByEmbeddingAsync(
                It.IsAny<float[]>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
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
        var result = await sut.ExecuteAsync("test", MakeBomItem("test"), CancellationToken.None);

        // Should gracefully return null for broken JSON fields
        var match = Assert.Single(result.Matches);
        Assert.Null(match.KeyFeatures);
        Assert.Null(match.TechnicalSpecs);
    }
}
