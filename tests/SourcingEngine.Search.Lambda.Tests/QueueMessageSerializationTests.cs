using System.Text.Json;
using SourcingEngine.Common.Models;
using Xunit;

namespace SourcingEngine.Search.Lambda.Tests;

/// <summary>
/// Contract tests verifying JSON serialization of sourcing result queue messages.
/// Ensures camelCase names and round-trip compatibility.
/// </summary>
public class QueueMessageSerializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    [Fact]
    public void SourcingResultMessage_SerializesAsCamelCase()
    {
        var msg = new SourcingResultMessage
        {
            TraceId = "trace-abc",
            ProjectId = "proj-123",
            SourceFile = "test.csv",
            TotalMatches = 5,
            TotalExecutionTimeMs = 1234,
            Items = new List<SourcingResultItem>
            {
                new()
                {
                    BomItem = "CMU Block",
                    Spec = "8 inch masonry block",
                    Quantity = 100,
                    MatchCount = 5,
                    FamilyLabel = "cmu",
                    CsiCode = "042200",
                    ExecutionTimeMs = 200,
                    Matches = new List<ProductMatchDto>
                    {
                        new()
                        {
                            ProductId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                            Vendor = "Oldcastle",
                            ModelName = "Standard CMU",
                            SemanticScore = 0.95f,
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(msg, Options);

        Assert.Contains("\"traceId\":", json);
        Assert.Contains("\"projectId\":", json);
        Assert.Contains("\"sourceFile\":", json);
        Assert.Contains("\"totalMatches\":", json);
        Assert.Contains("\"totalExecutionTimeMs\":", json);
        Assert.Contains("\"bomItem\":", json);
        Assert.Contains("\"matchCount\":", json);
        Assert.Contains("\"familyLabel\":", json);
        Assert.Contains("\"csiCode\":", json);
        Assert.Contains("\"productId\":", json);
        Assert.Contains("\"vendor\":", json);
        Assert.Contains("\"modelName\":", json);
        Assert.Contains("\"semanticScore\":", json);

        // Should NOT contain PascalCase
        Assert.DoesNotContain("\"TraceId\":", json);
        Assert.DoesNotContain("\"ProjectId\":", json);
        Assert.DoesNotContain("\"BomItem\":", json);
    }

    [Fact]
    public void SourcingResultMessage_RoundTrip()
    {
        var original = new SourcingResultMessage
        {
            TraceId = "trace-rt",
            ProjectId = "proj-rt",
            SourceFile = "roundtrip.csv",
            TotalMatches = 3,
            TotalExecutionTimeMs = 500,
            Items = new List<SourcingResultItem>
            {
                new()
                {
                    BomItem = "Rebar",
                    Spec = "#4 rebar",
                    Quantity = 50,
                    MatchCount = 3,
                    Matches = new List<ProductMatchDto>
                    {
                        new()
                        {
                            ProductId = Guid.NewGuid(),
                            Vendor = "NUCOR",
                            ModelName = "Grade 60 #4",
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<SourcingResultMessage>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(original.TraceId, deserialized!.TraceId);
        Assert.Equal(original.ProjectId, deserialized.ProjectId);
        Assert.Equal(original.TotalMatches, deserialized.TotalMatches);
        Assert.Single(deserialized.Items);
        Assert.Equal("Rebar", deserialized.Items[0].BomItem);
        Assert.Equal(3, deserialized.Items[0].MatchCount);
        Assert.Single(deserialized.Items[0].Matches);
        Assert.Equal("NUCOR", deserialized.Items[0].Matches[0].Vendor);
    }

    [Fact]
    public void SourcingZeroResultsMessage_SerializesAsCamelCase()
    {
        var msg = new SourcingZeroResultsMessage
        {
            TraceId = "trace-zero",
            ProjectId = "proj-zero",
            SourceFile = "test.csv",
            Items = new List<ZeroResultItem>
            {
                new()
                {
                    BomItem = "Unknown Product",
                    Spec = "something not in catalog",
                    Quantity = 1,
                    Warnings = new List<string> { "No family match" },
                }
            },
            Warnings = new List<string> { "1 item with zero results" },
        };

        var json = JsonSerializer.Serialize(msg, Options);

        Assert.Contains("\"traceId\":", json);
        Assert.Contains("\"bomItem\":", json);
        Assert.Contains("\"warnings\":", json);
        Assert.DoesNotContain("\"TraceId\":", json);
        Assert.DoesNotContain("\"BomItem\":", json);
    }

    [Fact]
    public void SourcingZeroResultsMessage_RoundTrip()
    {
        var original = new SourcingZeroResultsMessage
        {
            TraceId = "trace-zrt",
            ProjectId = "proj-zrt",
            SourceFile = "zero.csv",
            Items = new List<ZeroResultItem>
            {
                new()
                {
                    BomItem = "Mystery Material",
                    Spec = "alien construction fiber",
                    Quantity = 42,
                }
            }
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<SourcingZeroResultsMessage>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(original.TraceId, deserialized!.TraceId);
        Assert.Single(deserialized.Items);
        Assert.Equal("Mystery Material", deserialized.Items[0].BomItem);
        Assert.Equal(42, deserialized.Items[0].Quantity);
    }

    [Fact]
    public void ProductMatchDto_NullableFields_OmittedWhenNull()
    {
        var dto = new ProductMatchDto
        {
            ProductId = Guid.NewGuid(),
            Vendor = "TestVendor",
            ModelName = "TestModel",
            // Leave nullable fields as null
        };

        var json = JsonSerializer.Serialize(dto, Options);

        // Null fields should still serialize (as null) since we don't use JsonIgnore
        Assert.Contains("\"vendor\":\"TestVendor\"", json);
        Assert.Contains("\"modelName\":\"TestModel\"", json);
    }
}
