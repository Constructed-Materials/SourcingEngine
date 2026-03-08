using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SourcingEngine.Common.Models;
using SourcingEngine.Core.Configuration;
using SourcingEngine.Core.Models;
using SourcingEngine.Core.Services;
using System.Net;
using System.Text;
using System.Text.Json;
using Moq.Protected;
using Xunit;

namespace SourcingEngine.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="AgentSearchStrategy"/>.
/// Tests response parsing, user message building, and error handling
/// without hitting real Bedrock or Supabase endpoints.
/// </summary>
public class AgentSearchStrategyTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<AgentSearchStrategy>> _loggerMock;
    private readonly Mock<ILogger<SupabaseMcpPlugin>> _pluginLoggerMock;
    private readonly AgentSettings _settings;

    public AgentSearchStrategyTests()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<AgentSearchStrategy>>();
        _pluginLoggerMock = new Mock<ILogger<SupabaseMcpPlugin>>();
        _settings = new AgentSettings
        {
            Enabled = true,
            ModelId = "us.amazon.nova-pro-v1:0",
            Region = "us-east-2",
            SupabaseMcpUrl = "https://mcp.supabase.com/mcp?project_ref=test",
            SupabaseMcpAuthToken = "test-token",
            MaxToolCalls = 5,
            Temperature = 0.1f,
            MaxTokens = 4096,
            MaxResults = 10
        };

        // Default HttpClient mock
        _httpClientFactoryMock.Setup(f => f.CreateClient("SupabaseMcp"))
            .Returns(new HttpClient());
    }

    // ── SupabaseMcpPlugin tests ─────────────────────────────────

    [Fact]
    public async Task SupabaseMcpPlugin_RejectsNonSelectQuery()
    {
        var plugin = CreatePlugin();

        var result = await plugin.ExecuteSqlAsync("DELETE FROM products WHERE 1=1");

        Assert.Contains("Only SELECT queries are allowed", result);
    }

    [Theory]
    [InlineData("INSERT INTO products VALUES (1)")]
    [InlineData("UPDATE products SET is_active = false")]
    [InlineData("DROP TABLE products")]
    [InlineData("  drop table products")]
    public async Task SupabaseMcpPlugin_RejectsModifyingQueries(string query)
    {
        var plugin = CreatePlugin();

        var result = await plugin.ExecuteSqlAsync(query);

        Assert.Contains("Only SELECT queries are allowed", result);
    }

    [Fact]
    public async Task SupabaseMcpPlugin_AllowsSelectQuery()
    {
        var handler = new McpMockHandler();
        var httpClient = new HttpClient(handler);
        var plugin = new SupabaseMcpPlugin(httpClient, "https://test.com/mcp", "token", _pluginLoggerMock.Object);

        var result = await plugin.ExecuteSqlAsync("SELECT * FROM products WHERE is_active = true LIMIT 5");

        Assert.Contains("product_id", result);
    }

    [Fact]
    public async Task SupabaseMcpPlugin_AllowsWithCteQuery()
    {
        var handler = new McpMockHandler();
        var httpClient = new HttpClient(handler);
        var plugin = new SupabaseMcpPlugin(httpClient, "https://test.com/mcp", "token", _pluginLoggerMock.Object);

        var result = await plugin.ExecuteSqlAsync("WITH families AS (SELECT * FROM cm_master_materials) SELECT * FROM families");

        Assert.DoesNotContain("Only SELECT queries are allowed", result);
    }

    [Fact]
    public async Task SupabaseMcpPlugin_HandlesHttpError()
    {
        var handler = new McpMockHandler(toolCallStatusCode: HttpStatusCode.InternalServerError);
        var httpClient = new HttpClient(handler);
        var plugin = new SupabaseMcpPlugin(httpClient, "https://test.com/mcp", "token", _pluginLoggerMock.Object);

        var result = await plugin.ExecuteSqlAsync("SELECT 1");

        Assert.Contains("MCP error", result);
    }

    // ── Response parsing (via reflection for private methods) ────

    [Fact]
    public void ExtractJson_PlainJson_ReturnsAsIs()
    {
        var json = """{"family_label":"cmu_blocks","matches":[]}""";
        var result = InvokeExtractJson(json);
        Assert.Equal(json, result);
    }

    [Fact]
    public void ExtractJson_MarkdownJsonFence_StripsIt()
    {
        var input = """
            ```json
            {"family_label":"cmu_blocks","matches":[]}
            ```
            """;
        var result = InvokeExtractJson(input);
        Assert.Contains("cmu_blocks", result);
        Assert.DoesNotContain("```", result);
    }

    [Fact]
    public void ExtractJson_MarkdownGenericFence_StripsIt()
    {
        var input = """
            ```
            {"family_label":"cmu_blocks","matches":[]}
            ```
            """;
        var result = InvokeExtractJson(input);
        Assert.Contains("cmu_blocks", result);
        Assert.DoesNotContain("```", result);
    }

    [Fact]
    public void ExtractJson_ThinkingTags_StripsAndExtractsJson()
    {
        var input = """
            <thinking>
            The user wants masonry blocks. Let me search for CMU products.
            </thinking>

            {"family_label":"cmu_blocks","matches":[]}
            """;
        var result = InvokeExtractJson(input);
        Assert.Contains("cmu_blocks", result);
        Assert.DoesNotContain("thinking", result);
    }

    [Fact]
    public void ExtractJson_SurroundingText_ExtractsBraces()
    {
        var input = """Here are the results: {"family_label":"cmu_blocks","matches":[]} Hope this helps!""";
        var result = InvokeExtractJson(input);
        Assert.StartsWith("{", result);
        Assert.EndsWith("}", result);
        Assert.Contains("cmu_blocks", result);
    }

    [Fact]
    public void ParseAgentResponse_ValidJson_ReturnsCorrectResult()
    {
        var productId = Guid.NewGuid();
        var json = $$"""
        {
          "family_label": "cmu_blocks",
          "csi_code": "042200",
          "matches": [
            {
              "product_id": "{{productId}}",
              "model_name": "8in Standard CMU",
              "vendor": "CEMEX",
              "csi_code": "042200",
              "description": "Standard weight CMU block",
              "use_cases": ["load-bearing walls", "foundation"],
              "specifications": {"width_inches": 7.625, "nominal_size": "8X8X16"},
              "score": 0.95,
              "reasoning": "Exact match for 8 inch CMU block"
            }
          ],
          "warnings": []
        }
        """;

        var strategy = CreateStrategy();
        var result = InvokeParseAgentResponse(strategy, json);

        Assert.Equal("cmu_blocks", result.FamilyLabel);
        Assert.Equal("042200", result.CsiCode);
        Assert.Single(result.Matches);
        Assert.Equal(productId, result.Matches[0].ProductId);
        Assert.Equal("8in Standard CMU", result.Matches[0].ModelName);
        Assert.Equal("CEMEX", result.Matches[0].Vendor);
        Assert.Equal(0.95f, result.Matches[0].FinalScore);
        Assert.Equal("Exact match for 8 inch CMU block", result.Matches[0].Reasoning);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ParseAgentResponse_MultipleMatches_ReturnsAll()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var json = $$"""
        {
          "family_label": "cmu_blocks",
          "csi_code": "042200",
          "matches": [
            {"product_id": "{{id1}}", "model_name": "Model A", "vendor": "V1", "score": 0.9},
            {"product_id": "{{id2}}", "model_name": "Model B", "vendor": "V2", "score": 0.8}
          ],
          "warnings": ["Some minor issue"]
        }
        """;

        var strategy = CreateStrategy();
        var result = InvokeParseAgentResponse(strategy, json);

        Assert.Equal(2, result.Matches.Count);
        Assert.Single(result.Warnings);
        Assert.Contains("Some minor issue", result.Warnings);
    }

    [Fact]
    public void ParseAgentResponse_InvalidProductId_SkipsMatch()
    {
        var json = """
        {
          "family_label": "cmu_blocks",
          "matches": [
            {"product_id": "not-a-guid", "model_name": "Bad", "vendor": "V1", "score": 0.5}
          ],
          "warnings": []
        }
        """;

        var strategy = CreateStrategy();
        var result = InvokeParseAgentResponse(strategy, json);

        Assert.Empty(result.Matches);
    }

    [Fact]
    public void ParseAgentResponse_InvalidJson_ReturnsWarning()
    {
        var strategy = CreateStrategy();
        var result = InvokeParseAgentResponse(strategy, "This is not JSON at all");

        Assert.Empty(result.Matches);
        Assert.Single(result.Warnings);
        Assert.Contains("unparseable", result.Warnings[0]);
    }

    [Fact]
    public void ParseAgentResponse_EmptyMatches_ReturnsEmptyList()
    {
        var json = """{"family_label": "cmu_blocks", "matches": [], "warnings": ["No products found"]}""";

        var strategy = CreateStrategy();
        var result = InvokeParseAgentResponse(strategy, json);

        Assert.Empty(result.Matches);
        Assert.Equal("cmu_blocks", result.FamilyLabel);
        Assert.Contains("No products found", result.Warnings);
    }

    [Fact]
    public void ParseAgentResponse_NullFamilyLabel_ReturnsNull()
    {
        var json = """{"family_label": null, "csi_code": null, "matches": [], "warnings": []}""";

        var strategy = CreateStrategy();
        var result = InvokeParseAgentResponse(strategy, json);

        Assert.Null(result.FamilyLabel);
        Assert.Null(result.CsiCode);
    }

    [Fact]
    public void ParseAgentResponse_WithUseCasesAndSpecs_ParsesCorrectly()
    {
        var productId = Guid.NewGuid();
        var json = $$"""
        {
          "family_label": "natural_stone",
          "csi_code": "044200",
          "matches": [
            {
              "product_id": "{{productId}}",
              "model_name": "Silver Grey Granite",
              "vendor": "StoneVendor",
              "description": "Flamed finish granite slab",
              "use_cases": ["exterior cladding", "flooring"],
              "specifications": {"thickness_mm": 30, "finish": "flamed"},
              "score": 0.88
            }
          ],
          "warnings": []
        }
        """;

        var strategy = CreateStrategy();
        var result = InvokeParseAgentResponse(strategy, json);

        var match = result.Matches[0];
        Assert.Equal(2, match.UseCases!.Count);
        Assert.Contains("exterior cladding", match.UseCases);
        Assert.NotNull(match.TechnicalSpecs);
        Assert.Equal("Flamed finish granite slab", match.Description);
    }

    // ── BuildUserMessage tests (via reflection) ─────────────────

    [Fact]
    public void BuildUserMessage_BasicItem_IncludesNameAndDescription()
    {
        var item = new BomLineItem { BomItem = "Masonry Block", Description = "8 inch CMU block" };

        var result = InvokeBuildUserMessage(item);

        Assert.Contains("Masonry Block", result);
        Assert.Contains("8 inch CMU block", result);
    }

    [Fact]
    public void BuildUserMessage_WithAllFields_IncludesEverything()
    {
        var item = new BomLineItem
        {
            BomItem = "Granite Slab",
            Description = "Silver Grey Granite, 30mm",
            Category = "Exterior Shell",
            Material = "granite",
            Quantity = 500,
            Uom = "sq ft",
            Certifications = ["ASTM C615", "LEED v5"],
            Notes = "Flamed finish required",
            TechnicalSpecs =
            [
                new TechnicalSpecItem { Name = "thickness", Value = 30.0, Uom = "mm" },
                new TechnicalSpecItem { Name = "finish", Value = "flamed" }
            ]
        };

        var result = InvokeBuildUserMessage(item);

        Assert.Contains("Granite Slab", result);
        Assert.Contains("Exterior Shell", result);
        Assert.Contains("granite", result);
        Assert.Contains("500", result);
        Assert.Contains("sq ft", result);
        Assert.Contains("ASTM C615", result);
        Assert.Contains("LEED v5", result);
        Assert.Contains("Flamed finish required", result);
        Assert.Contains("thickness", result);
        Assert.Contains("30", result);
        Assert.Contains("mm", result);
    }

    [Fact]
    public void BuildUserMessage_NullOptionalFields_OmitsThem()
    {
        var item = new BomLineItem { BomItem = "Block", Description = "CMU block" };

        var result = InvokeBuildUserMessage(item);

        Assert.DoesNotContain("Category", result);
        Assert.DoesNotContain("Material", result);
        Assert.DoesNotContain("Certifications", result);
        Assert.DoesNotContain("Notes", result);
        Assert.DoesNotContain("Quantity", result);
    }

    // ── ExtractSqlResult tests (MCP response parsing) ────────

    [Fact]
    public void ExtractSqlResult_UnwrapsContentAndUntrustedData()
    {
        var response = """
        {"result":{"content":[{"type":"text","text":"{\"result\":\"<untrusted-data-abc123>\\n[{\\\"count\\\":42}]\\n</untrusted-data-abc123>\"}"}]},"jsonrpc":"2.0","id":2}
        """;

        var result = SupabaseMcpPlugin.ExtractSqlResult(response);

        Assert.Equal("[{\"count\":42}]", result);
    }

    [Fact]
    public void ExtractSqlResult_HandlesJsonRpcError()
    {
        var response = """{"error":{"code":-32000,"message":"Not found"},"jsonrpc":"2.0","id":1}""";

        var result = SupabaseMcpPlugin.ExtractSqlResult(response);

        Assert.Contains("Not found", result);
    }

    [Fact]
    public void ExtractSqlResult_HandlesEmptyContent()
    {
        var response = """{"result":{"content":[]},"jsonrpc":"2.0","id":1}""";

        var result = SupabaseMcpPlugin.ExtractSqlResult(response);

        Assert.Contains("content", result);
    }

    // ── Helpers ─────────────────────────────────────────────────

    private SupabaseMcpPlugin CreatePlugin()
    {
        return new SupabaseMcpPlugin(
            new HttpClient(),
            "https://test.com/mcp",
            "test-token",
            _pluginLoggerMock.Object);
    }

    private AgentSearchStrategy CreateStrategy()
    {
        return new AgentSearchStrategy(
            Options.Create(_settings),
            _httpClientFactoryMock.Object,
            _loggerMock.Object,
            _pluginLoggerMock.Object);
    }

    /// <summary>Invoke private static ExtractJson via reflection.</summary>
    private static string InvokeExtractJson(string input)
    {
        var method = typeof(AgentSearchStrategy)
            .GetMethod("ExtractJson", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (string)method.Invoke(null, [input])!;
    }

    /// <summary>Invoke private ParseAgentResponse via reflection.</summary>
    private SearchStrategyResult InvokeParseAgentResponse(AgentSearchStrategy strategy, string responseText)
    {
        var method = typeof(AgentSearchStrategy)
            .GetMethod("ParseAgentResponse", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var item = new BomLineItem { BomItem = "test", Description = "test item" };
        return (SearchStrategyResult)method.Invoke(strategy, [responseText, item])!;
    }

    /// <summary>Invoke private static BuildUserMessage via reflection.</summary>
    private static string InvokeBuildUserMessage(BomLineItem item)
    {
        var method = typeof(AgentSearchStrategy)
            .GetMethod("BuildUserMessage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (string)method.Invoke(null, [item])!;
    }

    /// <summary>
    /// Mock HTTP handler that simulates the MCP Streamable HTTP protocol:
    /// first request = initialize (returns session ID in header),
    /// subsequent requests = tools/call (returns MCP-formatted result).
    /// </summary>
    private class McpMockHandler : HttpMessageHandler
    {
        private int _callCount;
        private readonly HttpStatusCode _toolCallStatusCode;

        public McpMockHandler(HttpStatusCode toolCallStatusCode = HttpStatusCode.OK)
        {
            _toolCallStatusCode = toolCallStatusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _callCount);

            if (call == 1)
            {
                // Initialize response
                var initResponse = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"result":{"protocolVersion":"2024-11-05","capabilities":{"tools":{}},"serverInfo":{"name":"supabase","version":"0.7.0"}},"jsonrpc":"2.0","id":1}""",
                        Encoding.UTF8, "application/json")
                };
                initResponse.Headers.Add("Mcp-Session-Id", "test-session-id");
                return Task.FromResult(initResponse);
            }

            // tools/call response
            if (_toolCallStatusCode != HttpStatusCode.OK)
            {
                return Task.FromResult(new HttpResponseMessage(_toolCallStatusCode)
                {
                    Content = new StringContent("Server error", Encoding.UTF8, "text/plain")
                });
            }

            var toolResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"result":{"content":[{"type":"text","text":"{\"result\":\"<untrusted-data-test>\\n[{\\\"product_id\\\":\\\"abc\\\"}]\\n</untrusted-data-test>\"}"}]},"jsonrpc":"2.0","id":2}""",
                    Encoding.UTF8, "application/json")
            };
            return Task.FromResult(toolResponse);
        }
    }
}
