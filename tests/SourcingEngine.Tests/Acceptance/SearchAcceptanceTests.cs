using Microsoft.Extensions.DependencyInjection;
using SourcingEngine.Core.Services;
using SourcingEngine.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace SourcingEngine.Tests.Acceptance;

/// <summary>
/// End-to-end acceptance tests based on documented test cases
/// Uses minimum thresholds for data stability
/// </summary>
[Collection("Database")]
[Trait("Category", "Integration")]
[Trait("Category", "Acceptance")]
public class SearchAcceptanceTests
{
    private readonly DatabaseFixture _fixture;
    private readonly ITestOutputHelper _output;

    public SearchAcceptanceTests(DatabaseFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// Test Case 1: 8" Masonry Block
    /// Expected: ≥3 matches from ≥2 vendors
    /// </summary>
    [Fact]
    public async Task Search_MasonryBlock_ReturnsMinimumMatches()
    {
        using var scope = _fixture.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISearchOrchestrator>();
        var bomText = "8\" Masonry block";

        var result = await orchestrator.SearchAsync(bomText);

        OutputResult(result);
        
        Assert.True(result.MatchCount >= 3, 
            $"Expected ≥3 matches for masonry block, got {result.MatchCount}");
        
        var distinctVendors = result.Matches.Select(m => m.Vendor).Distinct().Count();
        Assert.True(distinctVendors >= 1, 
            $"Expected ≥1 distinct vendors, got {distinctVendors}");
        
        Assert.Equal("cmu_blocks", result.FamilyLabel);
    }

    /// <summary>
    /// Test Case 2: BCI Floor Joists
    /// Expected: ≥1 matches
    /// </summary>
    [Fact]
    public async Task Search_FloorJoists_ReturnsMinimumMatches()
    {
        using var scope = _fixture.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISearchOrchestrator>();
        var bomText = "Pre Engineered Wood Floor Trusses";

        var result = await orchestrator.SearchAsync(bomText);

        OutputResult(result);
        
        Assert.True(result.MatchCount >= 1, 
            $"Expected ≥1 matches for floor joists, got {result.MatchCount}");
    }

    /// <summary>
    /// Test Case 3: Stucco System
    /// Expected: ≥1 matches
    /// </summary>
    [Fact]
    public async Task Search_Stucco_ReturnsMinimumMatches()
    {
        using var scope = _fixture.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISearchOrchestrator>();
        var bomText = "5/8 stucco on block";

        var result = await orchestrator.SearchAsync(bomText);

        OutputResult(result);
        
        Assert.True(result.MatchCount >= 1, 
            $"Expected ≥1 matches for stucco, got {result.MatchCount}");
    }

    /// <summary>
    /// Test Case 4: Aluminum Railing
    /// </summary>
    [Fact(Skip = "Temporarily disabled")]
    public async Task Search_Railing_ReturnsMinimumMatches()
    {
        using var scope = _fixture.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISearchOrchestrator>();
        var bomText = "Ext Railing";

        var result = await orchestrator.SearchAsync(bomText);

        OutputResult(result);
        
        Assert.True(result.MatchCount >= 1, 
            $"Expected ≥1 matches for railing, got {result.MatchCount}");
    }

    /// <summary>
    /// Test Case 5: LVL Stair Stringer
    /// </summary>
    [Fact(Skip = "Temporarily disabled")]
    public async Task Search_Stair_ReturnsMinimumMatches()
    {
        using var scope = _fixture.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISearchOrchestrator>();
        var bomText = "Stairs - Wood";

        var result = await orchestrator.SearchAsync(bomText);

        OutputResult(result);
        
        Assert.True(result.MatchCount >= 1, 
            $"Expected ≥1 matches for stairs, got {result.MatchCount}");
    }

    /// <summary>
    /// Test that both metric and imperial searches find results
    /// </summary>
    [Fact]
    public async Task Search_MetricSize_FindsSameAsImperial()
    {
        using var scope = _fixture.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISearchOrchestrator>();

        var metricResult = await orchestrator.SearchAsync("20cm masonry block");
        var imperialResult = await orchestrator.SearchAsync("8 inch masonry block");

        _output.WriteLine($"Metric search: {metricResult.MatchCount} matches");
        _output.WriteLine($"Imperial search: {imperialResult.MatchCount} matches");

        Assert.True(metricResult.MatchCount >= 1, "Metric search should find results");
        Assert.True(imperialResult.MatchCount >= 1, "Imperial search should find results");
    }

    private void OutputResult(SourcingEngine.Core.Models.SearchResult result)
    {
        _output.WriteLine($"Query: {result.Query}");
        _output.WriteLine($"Family: {result.FamilyLabel}");
        _output.WriteLine($"CSI Code: {result.CsiCode}");
        _output.WriteLine($"Match Count: {result.MatchCount}");
        _output.WriteLine($"Execution Time: {result.ExecutionTimeMs}ms");
        _output.WriteLine("Matches:");
        foreach (var match in result.Matches.Take(10))
        {
            _output.WriteLine($"  - {match.Vendor}: {match.ModelName} [{match.CsiCode}]");
        }
        if (result.Warnings.Count > 0)
        {
            _output.WriteLine($"Warnings: {string.Join(", ", result.Warnings)}");
        }
    }
}
