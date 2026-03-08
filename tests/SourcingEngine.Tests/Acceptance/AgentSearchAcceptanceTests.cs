using Microsoft.Extensions.DependencyInjection;
using SourcingEngine.Common.Models;
using SourcingEngine.Core.Models;
using SourcingEngine.Core.Services;
using SourcingEngine.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace SourcingEngine.Tests.Acceptance;

/// <summary>
/// End-to-end acceptance tests for the AI agent-based search strategy.
/// Uses <see cref="AgentDatabaseFixture"/> which loads appsettings.AgentTest.json
/// with Agent.Enabled=true, so all searches go through <see cref="AgentSearchStrategy"/>.
/// 
/// These tests hit real Bedrock and Supabase MCP endpoints.
/// </summary>
[Collection("AgentDatabase")]
[Trait("Category", "Integration")]
[Trait("Category", "Agent")]
public class AgentSearchAcceptanceTests
{
    private readonly AgentDatabaseFixture _fixture;
    private readonly ITestOutputHelper _output;

    public AgentSearchAcceptanceTests(AgentDatabaseFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// Verify the DI container resolves <see cref="AgentSearchStrategy"/> (not ProductFirst).
    /// </summary>
    [Fact]
    public void DI_ResolvesAgentSearchStrategy()
    {
        using var scope = _fixture.CreateScope();
        var strategy = scope.ServiceProvider.GetRequiredService<ISearchStrategy>();

        _output.WriteLine($"Resolved strategy type: {strategy.GetType().Name}");
        Assert.IsType<AgentSearchStrategy>(strategy);
    }

    /// <summary>
    /// Agent search for 8" Masonry Block — the canonical test case.
    /// Expected: agent finds cmu_blocks family and returns ≥1 product matches.
    /// </summary>
    [Fact]
    public async Task AgentSearch_MasonryBlock_ReturnsMatches()
    {
        using var scope = _fixture.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISearchOrchestrator>();

        var result = await orchestrator.SearchAsync("8 inch masonry block");

        OutputResult(result);

        Assert.True(result.MatchCount >= 1,
            $"Expected ≥1 matches for masonry block, got {result.MatchCount}");
    }

    /// <summary>
    /// Agent search for a BOM line item with full metadata.
    /// </summary>
    [Fact]
    public async Task AgentSearch_BomLineItem_WithSpecs_ReturnsMatches()
    {
        using var scope = _fixture.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISearchOrchestrator>();

        var bomLineItem = new BomLineItem
        {
            BomItem = "Masonry Block",
            Description = "8 inch standard weight Masonry Block (CMU)",
            Category = "Masonry",
            Material = "concrete",
            TechnicalSpecs =
            [
                new TechnicalSpecItem { Name = "width", Value = 8.0, Uom = "in" },
                new TechnicalSpecItem { Name = "height", Value = 8.0, Uom = "in" },
                new TechnicalSpecItem { Name = "length", Value = 16.0, Uom = "in" }
            ],
            Certifications = ["ASTM C90"],
            Quantity = 500,
            Uom = "Inch"
        };

        var request = new SourcingRequest
        {
            ExtractionResult = new ExtractionResultMessage
            {
                TraceId = Guid.NewGuid().ToString(),
                ProjectId = "agent-test",
                SourceFile = "test.csv",
                Items = [bomLineItem]
            }
        };

        var result = await orchestrator.SearchAsync(request);

        var itemResult = result.Items.First();
        _output.WriteLine($"BOM Item: {itemResult.BomItemName}");
        _output.WriteLine($"Family: {itemResult.SearchResult.FamilyLabel}");
        _output.WriteLine($"Matches: {itemResult.SearchResult.MatchCount}");
        _output.WriteLine($"Time: {itemResult.SearchResult.ExecutionTimeMs}ms");

        foreach (var match in itemResult.SearchResult.Matches.Take(5))
        {
            _output.WriteLine($"  - {match.Vendor}: {match.ModelName} (score: {match.FinalScore:F2})");
        }

        Assert.True(itemResult.SearchResult.MatchCount >= 1,
            $"Expected ≥1 matches for CMU block with specs, got {itemResult.SearchResult.MatchCount}");
    }

    /// <summary>
    /// Agent search for stucco — different material family.
    /// </summary>
    [Fact]
    public async Task AgentSearch_Stucco_ReturnsMatches()
    {
        using var scope = _fixture.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISearchOrchestrator>();

        var result = await orchestrator.SearchAsync("5/8 stucco on block");

        OutputResult(result);

        Assert.True(result.MatchCount >= 1,
            $"Expected ≥1 matches for stucco, got {result.MatchCount}");
    }

    /// <summary>
    /// Agent search for a natural stone product.
    /// </summary>
    [Fact]
    public async Task AgentSearch_NaturalStone_ReturnsMatches()
    {
        using var scope = _fixture.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISearchOrchestrator>();

        var bomLineItem = new BomLineItem
        {
            BomItem = "Granite (Silver Grey)",
            Description = "Granite (Silver Grey), Flamed, 30mm thickness",
            Category = "Exterior Shell",
            Material = "granite",
            TechnicalSpecs =
            [
                new TechnicalSpecItem { Name = "thickness", Value = 30.0, Uom = "mm" }
            ]
        };

        var request = new SourcingRequest
        {
            ExtractionResult = new ExtractionResultMessage
            {
                TraceId = Guid.NewGuid().ToString(),
                ProjectId = "agent-test",
                SourceFile = "test.csv",
                Items = [bomLineItem]
            }
        };

        var result = await orchestrator.SearchAsync(request);

        var itemResult = result.Items.First();
        _output.WriteLine($"BOM Item: {itemResult.BomItemName}");
        _output.WriteLine($"Family: {itemResult.SearchResult.FamilyLabel}");
        _output.WriteLine($"Matches: {itemResult.SearchResult.MatchCount}");

        foreach (var match in itemResult.SearchResult.Matches.Take(5))
        {
            _output.WriteLine($"  - {match.Vendor}: {match.ModelName} (score: {match.FinalScore:F2})");
        }

        Assert.True(itemResult.SearchResult.MatchCount >= 1,
            $"Expected ≥1 matches for granite, got {itemResult.SearchResult.MatchCount}");
    }

    /// <summary>
    /// Agent handles multiple BOM items in one request.
    /// </summary>
    [Fact]
    public async Task AgentSearch_MultipleBomItems_ReturnsResultsForEach()
    {
        using var scope = _fixture.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISearchOrchestrator>();

        var request = new SourcingRequest
        {
            ExtractionResult = new ExtractionResultMessage
            {
                TraceId = Guid.NewGuid().ToString(),
                ProjectId = "agent-test",
                SourceFile = "multi-test.csv",
                Items =
                [
                    new BomLineItem { BomItem = "CMU Block", Description = "8 inch masonry block" },
                    new BomLineItem { BomItem = "Stucco", Description = "5/8 stucco system" }
                ]
            }
        };

        var result = await orchestrator.SearchAsync(request);

        _output.WriteLine($"Total items: {result.Items.Count}");
        foreach (var item in result.Items)
        {
            _output.WriteLine($"  {item.BomItemName}: {item.SearchResult.MatchCount} matches, family={item.SearchResult.FamilyLabel}");
        }

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item =>
            Assert.True(item.SearchResult.MatchCount >= 0,
                $"Item '{item.BomItemName}' should have results or graceful zero"));
    }

    /// <summary>
    /// Agent search for floor trusses.
    /// </summary>
    [Fact]
    public async Task AgentSearch_FloorTrusses_ReturnsMatches()
    {
        using var scope = _fixture.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISearchOrchestrator>();

        var result = await orchestrator.SearchAsync("Pre Engineered Wood Floor Trusses");

        OutputResult(result);

        Assert.True(result.MatchCount >= 1,
            $"Expected ≥1 matches for floor trusses, got {result.MatchCount}");
    }

    private void OutputResult(SearchResult result)
    {
        _output.WriteLine($"Query: {result.Query}");
        _output.WriteLine($"Family: {result.FamilyLabel}");
        _output.WriteLine($"CSI Code: {result.CsiCode}");
        _output.WriteLine($"Match Count: {result.MatchCount}");
        _output.WriteLine($"Execution Time: {result.ExecutionTimeMs}ms");
        _output.WriteLine("Matches:");
        foreach (var match in result.Matches.Take(10))
        {
            _output.WriteLine($"  - {match.Vendor}: {match.ModelName} [{match.CsiCode}] (score: {match.FinalScore:F2})");
        }
        if (result.Warnings.Count > 0)
        {
            _output.WriteLine($"Warnings: {string.Join(", ", result.Warnings)}");
        }
    }
}
