using System.Text.Json;
using SourcingEngine.BomExtraction.Lambda.Models;
using SourcingEngine.BomExtraction.Models;

namespace SourcingEngine.BomExtraction.Lambda.Tests;

/// <summary>
/// Tests that message contracts serialize/deserialize correctly with camelCase JSON,
/// matching the Python worker contracts defined in BomDataExtractor.
/// </summary>
public class QueueMessageSerializationTests
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // =========================================================================
    // ExtractionRequestMessage tests
    // =========================================================================

    [Fact]
    public void ExtractionRequestMessage_Deserialize_PythonFormat()
    {
        // This is exactly what the Python worker publishes
        var json = """
        {
            "traceId": "abc-123",
            "projectId": "42",
            "bomFiles": [
                { "fileName": "estimate.csv", "url": "https://s3.amazonaws.com/bucket/estimate.csv" },
                { "fileName": "plan.pdf", "url": "s3://mybucket/plan.pdf" }
            ]
        }
        """;

        var msg = JsonSerializer.Deserialize<ExtractionRequestMessage>(json, CamelCase);

        Assert.NotNull(msg);
        Assert.Equal("abc-123", msg.TraceId);
        Assert.Equal("42", msg.ProjectId);
        Assert.Equal(2, msg.BomFiles.Count);
        Assert.Equal("estimate.csv", msg.BomFiles[0].FileName);
        Assert.Equal("https://s3.amazonaws.com/bucket/estimate.csv", msg.BomFiles[0].Url);
        Assert.Equal("plan.pdf", msg.BomFiles[1].FileName);
        Assert.Equal("s3://mybucket/plan.pdf", msg.BomFiles[1].Url);
    }

    [Fact]
    public void ExtractionRequestMessage_Serialize_ProducesCamelCase()
    {
        var msg = new ExtractionRequestMessage
        {
            TraceId = "xyz-789",
            ProjectId = "10",
            BomFiles = new List<BomFileReference>
            {
                new() { FileName = "test.xlsx", Url = "https://example.com/test.xlsx" }
            }
        };

        var json = JsonSerializer.Serialize(msg);

        Assert.Contains("\"traceId\":", json);
        Assert.Contains("\"projectId\":", json);
        Assert.Contains("\"bomFiles\":", json);
        Assert.Contains("\"fileName\":", json);
        Assert.Contains("\"url\":", json);
        // Should NOT contain PascalCase
        Assert.DoesNotContain("\"TraceId\":", json);
        Assert.DoesNotContain("\"ProjectId\":", json);
        Assert.DoesNotContain("\"BomFiles\":", json);
    }

    [Fact]
    public void ExtractionRequestMessage_EmptyBomFiles_Defaults()
    {
        var json = """{ "traceId": "empty-test", "projectId": "1" }""";
        var msg = JsonSerializer.Deserialize<ExtractionRequestMessage>(json, CamelCase);

        Assert.NotNull(msg);
        Assert.Equal("empty-test", msg.TraceId);
        Assert.Empty(msg.BomFiles);
    }

    // =========================================================================
    // ExtractionResultMessage tests
    // =========================================================================

    [Fact]
    public void ExtractionResultMessage_Serialize_ProducesCamelCase()
    {
        var msg = new ExtractionResultMessage
        {
            TraceId = "res-001",
            ProjectId = "42",
            SourceFile = "estimate.csv",
            SourceUrl = "https://example.com/estimate.csv",
            ItemCount = 2,
            Items = new List<BomLineItem>
            {
                new() { BomItem = "CMU Block", Spec = "8 inch masonry block" },
                new() { BomItem = "Rebar", Spec = "#4 rebar 20ft" },
            },
            Warnings = new List<string> { "Row 15 skipped: unrecognized format" },
            ModelUsed = "us.amazon.nova-pro-v1:0",
            InputTokens = 1200,
            OutputTokens = 450,
        };

        var json = JsonSerializer.Serialize(msg);

        Assert.Contains("\"traceId\":\"res-001\"", json);
        Assert.Contains("\"projectId\":\"42\"", json);
        Assert.Contains("\"sourceFile\":\"estimate.csv\"", json);
        Assert.Contains("\"sourceUrl\":", json);
        Assert.Contains("\"itemCount\":2", json);
        Assert.Contains("\"items\":", json);
        Assert.Contains("\"warnings\":", json);
        Assert.Contains("\"modelUsed\":", json);
        Assert.Contains("\"inputTokens\":1200", json);
        Assert.Contains("\"outputTokens\":450", json);
    }

    [Fact]
    public void ExtractionResultMessage_RoundTrip()
    {
        var original = new ExtractionResultMessage
        {
            TraceId = "round-trip",
            ProjectId = "7",
            SourceFile = "plan.pdf",
            SourceUrl = "https://s3/plan.pdf",
            ItemCount = 1,
            Items = new List<BomLineItem>
            {
                new() { BomItem = "Window", Spec = "6x4 double hung vinyl window" }
            },
            Warnings = new List<string>(),
            ModelUsed = "us.amazon.nova-pro-v1:0",
            InputTokens = 500,
            OutputTokens = 200,
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<ExtractionResultMessage>(json, CamelCase);

        Assert.NotNull(deserialized);
        Assert.Equal(original.TraceId, deserialized.TraceId);
        Assert.Equal(original.ProjectId, deserialized.ProjectId);
        Assert.Equal(original.SourceFile, deserialized.SourceFile);
        Assert.Equal(original.SourceUrl, deserialized.SourceUrl);
        Assert.Equal(original.ItemCount, deserialized.ItemCount);
        Assert.Single(deserialized.Items);
        Assert.Equal("Window", deserialized.Items[0].BomItem);
        Assert.Equal(original.ModelUsed, deserialized.ModelUsed);
        Assert.Equal(original.InputTokens, deserialized.InputTokens);
        Assert.Equal(original.OutputTokens, deserialized.OutputTokens);
    }

    [Fact]
    public void ExtractionResultMessage_NullTokens_SerializesCorrectly()
    {
        var msg = new ExtractionResultMessage
        {
            TraceId = "null-tokens",
            InputTokens = null,
            OutputTokens = null,
        };

        var json = JsonSerializer.Serialize(msg);
        var deserialized = JsonSerializer.Deserialize<ExtractionResultMessage>(json, CamelCase);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.InputTokens);
        Assert.Null(deserialized.OutputTokens);
    }
}
