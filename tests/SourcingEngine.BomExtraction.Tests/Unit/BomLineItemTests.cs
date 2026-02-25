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
        Assert.Equal(string.Empty, item.Spec);
        Assert.Null(item.Quantity);
        Assert.NotNull(item.AdditionalData);
        Assert.Empty(item.AdditionalData);
    }

    [Fact]
    public void BomLineItem_SetAllProperties_RoundTrips()
    {
        var item = new BomLineItem
        {
            BomItem = "Masonry Block",
            Spec = "8 inch CMU Block",
            Quantity = 1200,
            AdditionalData = new Dictionary<string, object?>
            {
                ["section"] = "Masonry",
                ["uom"] = "EA",
                ["unit_price"] = 3.50,
            }
        };

        Assert.Equal("Masonry Block", item.BomItem);
        Assert.Equal("8 inch CMU Block", item.Spec);
        Assert.Equal(1200, item.Quantity);
        Assert.Equal(3, item.AdditionalData.Count);
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
                new() { BomItem = "Item 1", Spec = "Spec 1" },
                new() { BomItem = "Item 2", Spec = "Spec 2" },
                new() { BomItem = "Item 3", Spec = "Spec 3" },
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
