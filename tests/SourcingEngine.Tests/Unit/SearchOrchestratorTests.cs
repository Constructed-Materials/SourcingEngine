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
/// Unit tests for the refactored SearchOrchestrator (thin router).
/// Validates input validation, strategy selection, and result assembly.
/// </summary>
public class SearchOrchestratorTests
{
    private readonly Mock<IInputNormalizer> _normalizerMock;
    private readonly Mock<IMaterialFamilyRepository> _familyRepoMock;
    private readonly Mock<ISearchStrategy> _familyFirstMock;
    private readonly Mock<ISearchStrategy> _productFirstMock;
    private readonly Mock<ISearchStrategy> _hybridMock;
    private readonly Mock<ILogger<SearchOrchestrator>> _loggerMock;
    private readonly SemanticSearchSettings _settings;

    public SearchOrchestratorTests()
    {
        _normalizerMock = new Mock<IInputNormalizer>();
        _familyRepoMock = new Mock<IMaterialFamilyRepository>();
        _familyFirstMock = new Mock<ISearchStrategy>();
        _productFirstMock = new Mock<ISearchStrategy>();
        _hybridMock = new Mock<ISearchStrategy>();
        _loggerMock = new Mock<ILogger<SearchOrchestrator>>();

        _settings = new SemanticSearchSettings
        {
            Enabled = true,
            DefaultMode = SemanticSearchMode.FamilyFirst
        };

        _familyFirstMock.SetupGet(s => s.Mode).Returns(SemanticSearchMode.FamilyFirst);
        _productFirstMock.SetupGet(s => s.Mode).Returns(SemanticSearchMode.ProductFirst);
        _hybridMock.SetupGet(s => s.Mode).Returns(SemanticSearchMode.Hybrid);

        _normalizerMock.Setup(n => n.Normalize(It.IsAny<string>()))
            .Returns<string>(text => new BomItem
            {
                RawText = text,
                Keywords = [text],
                Synonyms = [text],
                SizeVariants = []
            });

        // Default: family repo returns nothing (safe default)
        _familyRepoMock.Setup(r => r.FindByKeywordsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MaterialFamily>());
    }

    private SearchOrchestrator CreateOrchestrator(params ISearchStrategy[] strategies)
    {
        return new SearchOrchestrator(
            _normalizerMock.Object,
            _familyRepoMock.Object,
            strategies,
            Options.Create(_settings),
            _loggerMock.Object);
    }

    // ── Input validation ───────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_NullInput_ThrowsArgumentException()
    {
        var sut = CreateOrchestrator(_familyFirstMock.Object);
        await Assert.ThrowsAsync<ArgumentException>(() => sut.SearchAsync(null!));
    }

    [Fact]
    public async Task SearchAsync_EmptyInput_ThrowsArgumentException()
    {
        var sut = CreateOrchestrator(_familyFirstMock.Object);
        await Assert.ThrowsAsync<ArgumentException>(() => sut.SearchAsync(""));
    }

    [Fact]
    public async Task SearchAsync_WhitespaceInput_ThrowsArgumentException()
    {
        var sut = CreateOrchestrator(_familyFirstMock.Object);
        await Assert.ThrowsAsync<ArgumentException>(() => sut.SearchAsync("   "));
    }

    [Fact]
    public async Task SearchAsync_ExceedsMaxLength_ThrowsArgumentException()
    {
        var sut = CreateOrchestrator(_familyFirstMock.Object);
        var longInput = new string('x', 501);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => sut.SearchAsync(longInput));
        Assert.Contains("500", ex.Message);
    }

    [Fact]
    public async Task SearchAsync_ExactlyMaxLength_DoesNotThrow()
    {
        SetupStrategyReturnsEmpty(_familyFirstMock);
        var sut = CreateOrchestrator(_familyFirstMock.Object);
        var maxInput = new string('x', 500);
        var result = await sut.SearchAsync(maxInput);
        Assert.NotNull(result);
    }

    // ── Strategy selection ─────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_FamilyFirstMode_UsesFamilyFirstStrategy()
    {
        SetupStrategyReturnsEmpty(_familyFirstMock);
        var sut = CreateOrchestrator(_familyFirstMock.Object, _productFirstMock.Object);

        await sut.SearchAsync("cmu block", SemanticSearchMode.FamilyFirst);

        _familyFirstMock.Verify(s => s.ExecuteAsync(
            It.IsAny<string>(), It.IsAny<BomItem>(), It.IsAny<CancellationToken>()), Times.Once);
        _productFirstMock.Verify(s => s.ExecuteAsync(
            It.IsAny<string>(), It.IsAny<BomItem>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_ProductFirstMode_UsesProductFirstStrategy()
    {
        SetupStrategyReturnsEmpty(_productFirstMock);
        var sut = CreateOrchestrator(_familyFirstMock.Object, _productFirstMock.Object);

        await sut.SearchAsync("curtain wall", SemanticSearchMode.ProductFirst);

        _productFirstMock.Verify(s => s.ExecuteAsync(
            It.IsAny<string>(), It.IsAny<BomItem>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_HybridMode_UsesHybridStrategy()
    {
        SetupStrategyReturnsEmpty(_hybridMock);
        var sut = CreateOrchestrator(_familyFirstMock.Object, _productFirstMock.Object, _hybridMock.Object);

        await sut.SearchAsync("floor joist", SemanticSearchMode.Hybrid);

        _hybridMock.Verify(s => s.ExecuteAsync(
            It.IsAny<string>(), It.IsAny<BomItem>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_UnavailableMode_FallsBackToFamilyFirst()
    {
        SetupStrategyReturnsEmpty(_familyFirstMock);
        // Only register FamilyFirst — no ProductFirst available
        var sut = CreateOrchestrator(_familyFirstMock.Object);

        await sut.SearchAsync("cmu block", SemanticSearchMode.ProductFirst);

        // Should fall back to FamilyFirst
        _familyFirstMock.Verify(s => s.ExecuteAsync(
            It.IsAny<string>(), It.IsAny<BomItem>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_OffMode_FallsBackToFamilyFirst()
    {
        SetupStrategyReturnsEmpty(_familyFirstMock);
        var sut = CreateOrchestrator(_familyFirstMock.Object);

        // Off is not registered as a strategy, should fall back to FamilyFirst
        await sut.SearchAsync("block", SemanticSearchMode.Off);

        _familyFirstMock.Verify(s => s.ExecuteAsync(
            It.IsAny<string>(), It.IsAny<BomItem>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_DefaultMode_UsesSettingsDefaultMode()
    {
        _settings.DefaultMode = SemanticSearchMode.ProductFirst;
        SetupStrategyReturnsEmpty(_productFirstMock);

        var sut = CreateOrchestrator(_familyFirstMock.Object, _productFirstMock.Object);

        await sut.SearchAsync("cmu block"); // No explicit mode

        _productFirstMock.Verify(s => s.ExecuteAsync(
            It.IsAny<string>(), It.IsAny<BomItem>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_SemanticDisabled_SelectsOff()
    {
        _settings.Enabled = false;
        SetupStrategyReturnsEmpty(_familyFirstMock);
        var sut = CreateOrchestrator(_familyFirstMock.Object);

        await sut.SearchAsync("block"); // Should use Off → falls back to FamilyFirst

        _familyFirstMock.Verify(s => s.ExecuteAsync(
            It.IsAny<string>(), It.IsAny<BomItem>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Result assembly ────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_AssemblesSearchResult_WithCorrectFields()
    {
        var matches = new List<ProductMatch>
        {
            new() { ProductId = Guid.NewGuid(), Vendor = "Acme", ModelName = "W100" }
        };
        _familyFirstMock.Setup(s => s.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<BomItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchStrategyResult
            {
                Matches = matches,
                Warnings = ["Minor issue"],
                FamilyLabel = "cmu_blocks",
                CsiCode = "042200"
            });

        _familyRepoMock.Setup(r => r.FindByKeywordsAsync(
                It.Is<IEnumerable<string>>(k => k.Contains("cmu_blocks")), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MaterialFamily { FamilyLabel = "cmu_blocks", FamilyName = "CMU Blocks" }]);

        var sut = CreateOrchestrator(_familyFirstMock.Object);
        var result = await sut.SearchAsync("8 inch cmu", SemanticSearchMode.FamilyFirst);

        Assert.Equal("8 inch cmu", result.Query);
        Assert.Equal("cmu_blocks", result.FamilyLabel);
        Assert.Equal("042200", result.CsiCode);
        Assert.Single(result.Matches);
        Assert.Contains("Minor issue", result.Warnings);
        Assert.True(result.ExecutionTimeMs >= 0);
    }

    [Fact]
    public async Task SearchAsync_TrimsInput()
    {
        SetupStrategyReturnsEmpty(_familyFirstMock);
        var sut = CreateOrchestrator(_familyFirstMock.Object);

        var result = await sut.SearchAsync("  cmu block  ", SemanticSearchMode.FamilyFirst);

        Assert.Equal("cmu block", result.Query);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static void SetupStrategyReturnsEmpty(Mock<ISearchStrategy> mock)
    {
        mock.Setup(s => s.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<BomItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchStrategyResult());
    }
}
