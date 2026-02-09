using SourcingEngine.Core.Models;
using SourcingEngine.Core.Services;
using Xunit;

namespace SourcingEngine.Tests.Unit;

/// <summary>
/// Unit tests for HybridStrategy fusion logic.
/// Tests the static FuseProductMatches method directly (internal).
/// </summary>
public class HybridStrategyTests
{
    private static ProductMatch MakeMatch(string vendor, string model, float? score = null)
        => new() { ProductId = Guid.NewGuid(), Vendor = vendor, ModelName = model, SemanticScore = score };

    [Fact]
    public void FuseProductMatches_BothEmpty_ReturnsEmpty()
    {
        var result = HybridStrategy.FuseProductMatches([], [], 10);
        Assert.Empty(result);
    }

    [Fact]
    public void FuseProductMatches_OnlySemantic_ReturnsAll()
    {
        var semantic = new List<ProductMatch>
        {
            MakeMatch("V1", "M1", 0.9f),
            MakeMatch("V2", "M2", 0.8f),
        };

        var result = HybridStrategy.FuseProductMatches([], semantic, 10);

        Assert.Equal(2, result.Count);
        Assert.Equal("V1", result[0].Vendor);
    }

    [Fact]
    public void FuseProductMatches_OnlyFamily_ReturnsAll()
    {
        var family = new List<ProductMatch>
        {
            MakeMatch("V1", "M1"),
            MakeMatch("V2", "M2"),
        };

        var result = HybridStrategy.FuseProductMatches(family, [], 10);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FuseProductMatches_PrefersSemantic_InInterleaving()
    {
        var family = new List<ProductMatch> { MakeMatch("Family", "Prod1") };
        var semantic = new List<ProductMatch> { MakeMatch("Semantic", "Prod2", 0.95f) };

        var result = HybridStrategy.FuseProductMatches(family, semantic, 10);

        Assert.Equal(2, result.Count);
        Assert.Equal("Semantic", result[0].Vendor); // Semantic first
        Assert.Equal("Family", result[1].Vendor);
    }

    [Fact]
    public void FuseProductMatches_Deduplicates_ByVendorAndModel()
    {
        var family = new List<ProductMatch> { MakeMatch("V1", "M1") };
        var semantic = new List<ProductMatch> { MakeMatch("V1", "M1", 0.9f) };

        var result = HybridStrategy.FuseProductMatches(family, semantic, 10);

        // Same vendor+model → only one entry (semantic version, since it's tried first)
        Assert.Single(result);
        Assert.Equal(0.9f, result[0].SemanticScore);
    }

    [Fact]
    public void FuseProductMatches_RespectsLimit()
    {
        var family = new List<ProductMatch>
        {
            MakeMatch("F1", "M1"), MakeMatch("F2", "M2"), MakeMatch("F3", "M3"),
        };
        var semantic = new List<ProductMatch>
        {
            MakeMatch("S1", "M4", 0.9f), MakeMatch("S2", "M5", 0.8f), MakeMatch("S3", "M6", 0.7f),
        };

        var result = HybridStrategy.FuseProductMatches(family, semantic, 3);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void FuseProductMatches_InterleavesResults()
    {
        var family = new List<ProductMatch>
        {
            MakeMatch("F1", "MF1"), MakeMatch("F2", "MF2"),
        };
        var semantic = new List<ProductMatch>
        {
            MakeMatch("S1", "MS1", 0.9f), MakeMatch("S2", "MS2", 0.8f),
        };

        var result = HybridStrategy.FuseProductMatches(family, semantic, 10);

        // Expected order: S1, F1, S2, F2 (interleaved, semantic first each round)
        Assert.Equal(4, result.Count);
        Assert.Equal("S1", result[0].Vendor);
        Assert.Equal("F1", result[1].Vendor);
        Assert.Equal("S2", result[2].Vendor);
        Assert.Equal("F2", result[3].Vendor);
    }
}
