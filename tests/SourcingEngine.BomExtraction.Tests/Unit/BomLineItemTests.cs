using SourcingEngine.Common.Models;
using ExtractionResult = SourcingEngine.BomExtraction.Models.ExtractionResult;

namespace SourcingEngine.BomExtraction.Tests.Unit;

public class BomLineItemTests
{
    [Fact]
    public void BomLineItem_DefaultValues_AreCorrect()
    {
        var item = new BomLineItem();

        Assert.Equal(string.Empty, item.BomItem);
        Assert.Equal(string.Empty, item.Description);
        Assert.Null(item.Quantity);
        Assert.Null(item.Uom);
        Assert.Null(item.Category);
        Assert.Null(item.TechnicalSpecs);
        Assert.Null(item.Certifications);
        Assert.Null(item.Notes);
        Assert.NotNull(item.AdditionalData);
        Assert.Empty(item.AdditionalData);
    }

    [Fact]
    public void BomLineItem_SetAllProperties_RoundTrips()
    {
        var item = new BomLineItem
        {
            BomItem = "Masonry Block",
            Description = "8 inch CMU Block",
            Quantity = 1200,
            Uom = "EA",
            Category = "Masonry",
            TechnicalSpecs = new List<TechnicalSpecItem>
            {
                new() { Name = "width", Value = 8, Uom = "in" },
                new() { Name = "height", Value = 8, Uom = "in" },
            },
            Certifications = new List<string> { "ASTM C90", "LEED v5" },
            Notes = "Load-bearing walls only",
            AdditionalData = new Dictionary<string, object?>
            {
                ["unit_price"] = 3.50,
            }
        };

        Assert.Equal("Masonry Block", item.BomItem);
        Assert.Equal("8 inch CMU Block", item.Description);
        Assert.Equal(1200, item.Quantity);
        Assert.Equal("EA", item.Uom);
        Assert.Equal("Masonry", item.Category);
        Assert.Equal(2, item.TechnicalSpecs!.Count);
        Assert.Equal(2, item.Certifications!.Count);
        Assert.Contains("ASTM C90", item.Certifications);
        Assert.Equal("Load-bearing walls only", item.Notes);
        Assert.Single(item.AdditionalData);
    }

    [Fact]
    public void ExtractionResult_ItemCount_MatchesItemsList()
    {
        var result = new ExtractionResult
        {
            SourceFile = "test.csv",
            ModelUsed = "us.amazon.nova-pro-v1:0",
            Items = new List<BomLineItem>
            {
                new() { BomItem = "Item 1", Description = "Description 1" },
                new() { BomItem = "Item 2", Description = "Description 2" },
                new() { BomItem = "Item 3", Description = "Description 3" },
            }
        };

        Assert.Equal(3, result.ItemCount);
    }

    [Fact]
    public void ExtractionResult_DefaultValues_AreCorrect()
    {
        var result = new ExtractionResult();

        Assert.Equal(string.Empty, result.SourceFile);
        Assert.Equal(string.Empty, result.ModelUsed);
        Assert.Empty(result.Items);
        Assert.Empty(result.Warnings);
        Assert.Equal(0, result.ItemCount);
        Assert.Null(result.InputTokens);
        Assert.Null(result.OutputTokens);
    }
}
