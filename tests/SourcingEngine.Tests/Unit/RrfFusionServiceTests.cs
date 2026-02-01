using SourcingEngine.Core.Models;
using SourcingEngine.Core.Repositories;
using SourcingEngine.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace SourcingEngine.Tests.Unit;

/// <summary>
/// Unit tests for RrfFusionService
/// </summary>
public class RrfFusionServiceTests
{
    private readonly RrfFusionService _service;
    private readonly Mock<ILogger<RrfFusionService>> _loggerMock;

    public RrfFusionServiceTests()
    {
        _loggerMock = new Mock<ILogger<RrfFusionService>>();
        _service = new RrfFusionService(_loggerMock.Object);
    }

    [Fact]
    public void Fuse_WithEmptyLists_ReturnsEmpty()
    {
        // Arrange
        var ftsResults = new List<RankedMaterialFamily>();
        var semanticResults = new List<RankedMaterialFamily>();

        // Act
        var result = _service.Fuse(ftsResults, semanticResults);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Fuse_WithOnlyFtsResults_ReturnsRankedByFts()
    {
        // Arrange
        var ftsResults = new List<RankedMaterialFamily>
        {
            new(CreateFamily("cmu_blocks"), 1, 0.9f),
            new(CreateFamily("concrete"), 2, 0.7f),
            new(CreateFamily("masonry"), 3, 0.5f)
        };
        var semanticResults = new List<RankedMaterialFamily>();

        // Act
        var result = _service.Fuse(ftsResults, semanticResults, maxResults: 3);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("cmu_blocks", result[0].FamilyLabel);
        Assert.Equal("concrete", result[1].FamilyLabel);
        Assert.Equal("masonry", result[2].FamilyLabel);
    }

    [Fact]
    public void Fuse_WithOnlySemanticResults_ReturnsRankedBySemantic()
    {
        // Arrange
        var ftsResults = new List<RankedMaterialFamily>();
        var semanticResults = new List<RankedMaterialFamily>
        {
            new(CreateFamily("curtain_wall"), 1, 0.95f),
            new(CreateFamily("storefront"), 2, 0.85f)
        };

        // Act
        var result = _service.Fuse(ftsResults, semanticResults, maxResults: 2);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("curtain_wall", result[0].FamilyLabel);
        Assert.Equal("storefront", result[1].FamilyLabel);
    }

    [Fact]
    public void Fuse_WithOverlappingResults_CombinesScores()
    {
        // Arrange
        // cmu_blocks appears in both lists, should get higher combined score
        var ftsResults = new List<RankedMaterialFamily>
        {
            new(CreateFamily("cmu_blocks"), 1, 0.9f),
            new(CreateFamily("concrete"), 2, 0.7f)
        };
        var semanticResults = new List<RankedMaterialFamily>
        {
            new(CreateFamily("cmu_blocks"), 1, 0.95f),
            new(CreateFamily("masonry"), 2, 0.8f)
        };

        // Act
        var result = _service.Fuse(ftsResults, semanticResults, maxResults: 3);

        // Assert
        Assert.Equal(3, result.Count);
        // cmu_blocks should be first since it appears in both lists at rank 1
        Assert.Equal("cmu_blocks", result[0].FamilyLabel);
    }

    [Fact]
    public void Fuse_WithDifferentWeights_AffectsRanking()
    {
        // Arrange - items that don't overlap so we can test weight impact
        var ftsResults = new List<RankedMaterialFamily>
        {
            new(CreateFamily("fts_only"), 1, 0.9f)
        };
        var semanticResults = new List<RankedMaterialFamily>
        {
            new(CreateFamily("sem_only"), 1, 0.95f)
        };

        // Act - heavily weight semantic results
        var result = _service.Fuse(ftsResults, semanticResults, 
            fullTextWeight: 0.1f, semanticWeight: 2.0f, maxResults: 2);

        // Assert
        // sem_only should be ranked higher due to semantic weight (2.0 vs 0.1)
        Assert.Equal(2, result.Count);
        Assert.Equal("sem_only", result[0].FamilyLabel);
        Assert.Equal("fts_only", result[1].FamilyLabel);
    }

    [Fact]
    public void Fuse_WithHigherK_SmoothsRankDifferences()
    {
        // Arrange
        var ftsResults = new List<RankedMaterialFamily>
        {
            new(CreateFamily("first"), 1, 0.9f),
            new(CreateFamily("second"), 2, 0.7f)
        };
        var semanticResults = new List<RankedMaterialFamily>();

        // Act with low k (more impact from rank)
        var resultLowK = _service.Fuse(ftsResults, semanticResults, k: 1, maxResults: 2);
        
        // Act with high k (less impact from rank)
        var resultHighK = _service.Fuse(ftsResults, semanticResults, k: 100, maxResults: 2);

        // Assert - both should have same order but different score distributions
        Assert.Equal("first", resultLowK[0].FamilyLabel);
        Assert.Equal("first", resultHighK[0].FamilyLabel);
    }

    [Fact]
    public void Fuse_RespectsMaxResults()
    {
        // Arrange
        var ftsResults = Enumerable.Range(1, 10)
            .Select(i => new RankedMaterialFamily(CreateFamily($"family_{i}"), i, 1.0f / i))
            .ToList();
        var semanticResults = new List<RankedMaterialFamily>();

        // Act
        var result = _service.Fuse(ftsResults, semanticResults, maxResults: 3);

        // Assert
        Assert.Equal(3, result.Count);
    }

    private static MaterialFamily CreateFamily(string label) => new()
    {
        FamilyLabel = label,
        FamilyName = label.Replace("_", " "),
        CsiDivision = "04"
    };
}
