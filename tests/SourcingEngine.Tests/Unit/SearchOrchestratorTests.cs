using Microsoft.Extensions.Logging;
using Moq;
using SourcingEngine.Common.Models;
using SourcingEngine.Core.Models;
using SourcingEngine.Core.Services;
using Xunit;

namespace SourcingEngine.Tests.Unit;

/// <summary>
/// Unit tests for the refactored SearchOrchestrator.
/// Validates input validation, batch processing, and result assembly.
/// </summary>
public class SearchOrchestratorTests
{
    private readonly Mock<ISearchStrategy> _strategyMock;
    private readonly Mock<ILogger<SearchOrchestrator>> _loggerMock;

    public SearchOrchestratorTests()
    {
        _strategyMock = new Mock<ISearchStrategy>();
        _loggerMock = new Mock<ILogger<SearchOrchestrator>>();

        // Default: strategy returns empty result
        _strategyMock.Setup(s => s.ExecuteAsync(
                It.IsAny<BomLineItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchStrategyResult());
    }

    private SearchOrchestrator CreateOrchestrator()
    {
        return new SearchOrchestrator(
            _strategyMock.Object,
            _loggerMock.Object);
    }

    // ── Input validation (string overload) ─────────────────────────

    [Fact]
    public async Task SearchAsync_NullInput_ThrowsArgumentException()
    {
        var sut = CreateOrchestrator();
        await Assert.ThrowsAsync<ArgumentException>(() => sut.SearchAsync((string)null!));
    }

    [Fact]
    public async Task SearchAsync_EmptyInput_ThrowsArgumentException()
    {
        var sut = CreateOrchestrator();
        await Assert.ThrowsAsync<ArgumentException>(() => sut.SearchAsync(""));
    }

    [Fact]
    public async Task SearchAsync_WhitespaceInput_ThrowsArgumentException()
    {
        var sut = CreateOrchestrator();
        await Assert.ThrowsAsync<ArgumentException>(() => sut.SearchAsync("   "));
    }

    [Fact]
    public async Task SearchAsync_ExceedsMaxLength_ThrowsArgumentException()
    {
        var sut = CreateOrchestrator();
        var longInput = new string('x', 501);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => sut.SearchAsync(longInput));
        Assert.Contains("500", ex.Message);
    }

    [Fact]
    public async Task SearchAsync_ExactlyMaxLength_DoesNotThrow()
    {
        var sut = CreateOrchestrator();
        var maxInput = new string('x', 500);
        var result = await sut.SearchAsync(maxInput);
        Assert.NotNull(result);
    }

    // ── Input validation (SourcingRequest overload) ────────────────

    [Fact]
    public async Task SearchAsync_NullRequest_ThrowsArgumentNullException()
    {
        var sut = CreateOrchestrator();
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.SearchAsync((SourcingRequest)null!));
    }

    [Fact]
    public async Task SearchAsync_NullExtractionResult_ThrowsArgumentNullException()
    {
        var sut = CreateOrchestrator();
        var request = new SourcingRequest { ExtractionResult = null! };
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.SearchAsync(request));
    }

    // ── Batch processing ───────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_IteratesAllBomItems()
    {
        var request = MakeRequest(
            new BomLineItem { BomItem = "cmu block", Spec = "8 inch cmu block" },
            new BomLineItem { BomItem = "rebar", Spec = "#4 rebar" },
            new BomLineItem { BomItem = "stucco", Spec = "5/8 stucco" });

        var sut = CreateOrchestrator();
        var result = await sut.SearchAsync(request);

        Assert.Equal(3, result.Items.Count);
        _strategyMock.Verify(s => s.ExecuteAsync(
            It.IsAny<BomLineItem>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task SearchAsync_PropagatesMetadata()
    {
        var request = MakeRequest(
            new BomLineItem { BomItem = "cmu", Spec = "8 inch cmu", Quantity = 100 });
        request.ExtractionResult.TraceId = "trace-123";
        request.ExtractionResult.ProjectId = "proj-456";
        request.ExtractionResult.SourceFile = "estimate.csv";

        var sut = CreateOrchestrator();
        var result = await sut.SearchAsync(request);

        Assert.Equal("trace-123", result.TraceId);
        Assert.Equal("proj-456", result.ProjectId);
        Assert.Equal("estimate.csv", result.SourceFile);
    }

    [Fact]
    public async Task SearchAsync_CapturesItemQuantity()
    {
        var request = MakeRequest(
            new BomLineItem { BomItem = "cmu", Spec = "8 inch cmu block", Quantity = 250 });

        var sut = CreateOrchestrator();
        var result = await sut.SearchAsync(request);

        Assert.Equal(250, result.Items[0].Quantity);
    }

    // ── Error handling ─────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_StrategyFailsForOneItem_ContinuesOthers()
    {
        var request = MakeRequest(
            new BomLineItem { BomItem = "good1", Spec = "first item" },
            new BomLineItem { BomItem = "bad", Spec = "failing item" },
            new BomLineItem { BomItem = "good2", Spec = "third item" });

        _strategyMock.Setup(s => s.ExecuteAsync(
                It.Is<BomLineItem>(i => i.BomItem == "bad"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        var sut = CreateOrchestrator();
        var result = await sut.SearchAsync(request);

        // All 3 items should still be present
        Assert.Equal(3, result.Items.Count);
        // The failing item should have an empty result with warning
        var badItem = result.Items.First(i => i.BomItemName == "bad");
        Assert.Contains("Search failed", badItem.SearchResult.Warnings[0]);
        Assert.Contains("DB error", result.Warnings.First(w => w.Contains("bad")));
    }

    // ── Result assembly ────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_AssemblesSearchResult_WithCorrectFields()
    {
        var matches = new List<ProductMatch>
        {
            new() { ProductId = Guid.NewGuid(), Vendor = "Acme", ModelName = "W100" }
        };
        _strategyMock.Setup(s => s.ExecuteAsync(
                It.IsAny<BomLineItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchStrategyResult
            {
                Matches = matches,
                Warnings = ["Minor issue"],
                FamilyLabel = "cmu_blocks",
                CsiCode = "042200"
            });

        var sut = CreateOrchestrator();
        var result = await sut.SearchAsync("8 inch cmu");

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
        var sut = CreateOrchestrator();
        var result = await sut.SearchAsync("  cmu block  ");
        Assert.Equal("cmu block", result.Query);
    }

    [Fact]
    public async Task SearchAsync_SourcingResult_CalculatesTotalMatches()
    {
        _strategyMock.Setup(s => s.ExecuteAsync(
                It.IsAny<BomLineItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchStrategyResult
            {
                Matches = [
                    new ProductMatch { Vendor = "V1", ModelName = "M1" },
                    new ProductMatch { Vendor = "V2", ModelName = "M2" }
                ]
            });

        var request = MakeRequest(
            new BomLineItem { BomItem = "item1", Spec = "spec1" },
            new BomLineItem { BomItem = "item2", Spec = "spec2" });

        var sut = CreateOrchestrator();
        var result = await sut.SearchAsync(request);

        Assert.Equal(4, result.TotalMatches); // 2 matches × 2 items
        Assert.True(result.TotalExecutionTimeMs >= 0);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static SourcingRequest MakeRequest(params BomLineItem[] items)
    {
        return new SourcingRequest
        {
            ExtractionResult = new ExtractionResultMessage
            {
                TraceId = "test-trace",
                ProjectId = "test-project",
                SourceFile = "test.csv",
                Items = items.ToList(),
                Warnings = new List<string>()
            }
        };
    }
}
