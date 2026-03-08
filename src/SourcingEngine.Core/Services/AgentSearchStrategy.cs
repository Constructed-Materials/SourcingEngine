using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Amazon;
using Amazon.BedrockRuntime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using SourcingEngine.Common.Models;
using SourcingEngine.Core.Configuration;
using SourcingEngine.Core.Models;

namespace SourcingEngine.Core.Services;

/// <summary>
/// AI agent-based search strategy that uses a Bedrock LLM to reason about BOM items
/// and queries the Supabase database via MCP's execute_sql to find matching products.
/// 
/// Unlike <see cref="ProductFirstStrategy"/> which uses embeddings + vector similarity,
/// this strategy lets the LLM reason about the item, craft SQL queries, and evaluate
/// matches — enabling cross-item context and nuanced specification matching.
/// </summary>
public class AgentSearchStrategy : ISearchStrategy
{
    private readonly AgentSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AgentSearchStrategy> _logger;
    private readonly ILogger<SupabaseMcpPlugin> _pluginLogger;
    private readonly string _systemPrompt;

    public AgentSearchStrategy(
        IOptions<AgentSettings> settings,
        IHttpClientFactory httpClientFactory,
        ILogger<AgentSearchStrategy> logger,
        ILogger<SupabaseMcpPlugin> pluginLogger)
    {
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _pluginLogger = pluginLogger;
        _systemPrompt = LoadSystemPrompt();
    }

    public async Task<SearchStrategyResult> ExecuteAsync(
        BomLineItem item, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "AgentSearchStrategy: Searching for BOM item '{BomItem}' - '{Description}'",
            item.BomItem, item.Description);

        try
        {
            // Build the agent
            var kernel = CreateKernel();
            var agent = CreateAgent(kernel);

            // Build user message from BOM item
            var userMessage = BuildUserMessage(item);
            _logger.LogDebug("Agent user message: {Message}", userMessage);

            // Run the agent and collect the final response
            var responseText = await InvokeAgentAsync(agent, userMessage, cancellationToken);

            // Parse the agent's structured response
            return ParseAgentResponse(responseText, item);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent search failed for BOM item '{BomItem}'", item.BomItem);
            return new SearchStrategyResult
            {
                Matches = [],
                Warnings = [$"Agent search failed: {ex.Message}"],
                FamilyLabel = null,
                CsiCode = null
            };
        }
    }

    private Kernel CreateKernel()
    {
        var builder = Kernel.CreateBuilder();

        // Add Bedrock chat completion via Converse API
        // Use AddBedrockChatClient (IChatClient-based) instead of AddBedrockChatCompletionService
        // because the latter's internal factory doesn't route amazon.nova-* models.
        var bedrockRuntime = new AmazonBedrockRuntimeClient(
            RegionEndpoint.GetBySystemName(_settings.Region));

#pragma warning disable SKEXP0070
#pragma warning disable SKEXP0001
        builder.AddBedrockChatClient(_settings.ModelId, bedrockRuntime);
#pragma warning restore SKEXP0001
#pragma warning restore SKEXP0070

        // Add Supabase MCP plugin for database access
        var httpClient = _httpClientFactory.CreateClient("SupabaseMcp");
        var plugin = new SupabaseMcpPlugin(
            httpClient,
            _settings.SupabaseMcpUrl,
            _settings.SupabaseMcpAuthToken,
            _pluginLogger);

        builder.Plugins.AddFromObject(plugin, "SupabaseMcp");

        return builder.Build();
    }

    private ChatCompletionAgent CreateAgent(Kernel kernel)
    {
        var promptWithMaxResults = _systemPrompt.Replace(
            "{max_results}", _settings.MaxResults.ToString());

        return new ChatCompletionAgent
        {
            Name = "SourcingAgent",
            Instructions = promptWithMaxResults,
            Kernel = kernel,
            Arguments = new KernelArguments(
                new PromptExecutionSettings
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
                    ExtensionData = new Dictionary<string, object>
                    {
                        ["temperature"] = (double)_settings.Temperature,
                        ["max_tokens"] = _settings.MaxTokens
                    }
                })
        };
    }

    private static string BuildUserMessage(BomLineItem item)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Find matching products for this BOM line item:");
        sb.AppendLine();
        sb.AppendLine($"**Item:** {item.BomItem}");
        sb.AppendLine($"**Description:** {item.Description}");

        if (!string.IsNullOrWhiteSpace(item.Category))
            sb.AppendLine($"**Category:** {item.Category}");

        if (!string.IsNullOrWhiteSpace(item.Material))
            sb.AppendLine($"**Material:** {item.Material}");

        if (item.TechnicalSpecs?.Count > 0)
        {
            sb.AppendLine("**Technical Specifications:**");
            foreach (var spec in item.TechnicalSpecs)
            {
                var unit = string.IsNullOrEmpty(spec.Uom) ? "" : $" {spec.Uom}";
                sb.AppendLine($"  - {spec.Name}: {spec.Value}{unit}");
            }
        }

        if (item.Certifications?.Count > 0)
            sb.AppendLine($"**Required Certifications:** {string.Join(", ", item.Certifications)}");

        if (!string.IsNullOrWhiteSpace(item.Notes))
            sb.AppendLine($"**Notes:** {item.Notes}");

        if (item.Quantity.HasValue)
            sb.AppendLine($"**Quantity:** {item.Quantity} {item.Uom ?? "EA"}");

        if (item.AdditionalData?.Count > 0)
        {
            sb.AppendLine("**Additional Attributes:**");
            foreach (var kvp in item.AdditionalData)
            {
                if (kvp.Value is not null)
                    sb.AppendLine($"  - {kvp.Key}: {kvp.Value}");
            }
        }

        return sb.ToString();
    }

    private async Task<string> InvokeAgentAsync(
        ChatCompletionAgent agent,
        string userMessage,
        CancellationToken cancellationToken)
    {
        var chatHistory = new ChatHistory();
        chatHistory.AddUserMessage(userMessage);

        var responseBuilder = new StringBuilder();
        int toolCallCount = 0;

        await foreach (var response in agent.InvokeAsync(chatHistory, cancellationToken: cancellationToken))
        {
            ChatMessageContent message = response.Message;
            
            if (message.Content is not null)
            {
                responseBuilder.Append(message.Content);
            }

            // Safety: count tool calls to avoid runaway loops
            if (message.Role == AuthorRole.Tool)
            {
                toolCallCount++;
                if (toolCallCount > _settings.MaxToolCalls)
                {
                    _logger.LogWarning(
                        "Agent exceeded max tool calls ({Max}), stopping",
                        _settings.MaxToolCalls);
                    break;
                }
            }
        }

        var result = responseBuilder.ToString();
        _logger.LogInformation(
            "Agent completed with {ToolCalls} tool calls, response length: {Length}",
            toolCallCount, result.Length);

        return result;
    }

    private SearchStrategyResult ParseAgentResponse(string responseText, BomLineItem item)
    {
        // The agent should return pure JSON, but might wrap in markdown fences
        var json = ExtractJson(responseText);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var familyLabel = root.TryGetProperty("family_label", out var fl) && fl.ValueKind != JsonValueKind.Null
                ? fl.GetString()
                : null;

            var csiCode = root.TryGetProperty("csi_code", out var cc) && cc.ValueKind != JsonValueKind.Null
                ? cc.GetString()
                : null;

            var warnings = new List<string>();
            if (root.TryGetProperty("warnings", out var warningsEl) && warningsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var w in warningsEl.EnumerateArray())
                {
                    var text = w.GetString();
                    if (!string.IsNullOrEmpty(text))
                        warnings.Add(text);
                }
            }

            // Merge requirement_gaps into warnings so they surface in the response
            if (root.TryGetProperty("requirement_gaps", out var gapsEl) && gapsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var g in gapsEl.EnumerateArray())
                {
                    var text = g.GetString();
                    if (!string.IsNullOrEmpty(text))
                        warnings.Add($"[Gap] {text}");
                }
            }

            var matches = new List<ProductMatch>();
            if (root.TryGetProperty("matches", out var matchesEl) && matchesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in matchesEl.EnumerateArray())
                {
                    var match = ParseProductMatch(m);
                    if (match is not null)
                        matches.Add(match);
                }
            }

            _logger.LogInformation(
                "Agent found {MatchCount} matches for '{BomItem}', family={Family}, csi={Csi}",
                matches.Count, item.BomItem, familyLabel, csiCode);

            return new SearchStrategyResult
            {
                Matches = matches,
                Warnings = warnings,
                FamilyLabel = familyLabel,
                CsiCode = csiCode
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse agent response as JSON. Raw response: {Response}",
                responseText[..Math.Min(responseText.Length, 500)]);

            return new SearchStrategyResult
            {
                Matches = [],
                Warnings = [$"Agent returned unparseable response: {ex.Message}"],
                FamilyLabel = null,
                CsiCode = null
            };
        }
    }

    private ProductMatch? ParseProductMatch(JsonElement element)
    {
        try
        {
            var productIdStr = element.TryGetProperty("product_id", out var pid)
                ? pid.GetString()
                : null;

            if (string.IsNullOrEmpty(productIdStr) || !Guid.TryParse(productIdStr, out var productId))
            {
                _logger.LogWarning("Agent returned match without valid product_id");
                return null;
            }

            var vendor = element.TryGetProperty("vendor", out var v) ? v.GetString() ?? "Unknown" : "Unknown";
            var modelName = element.TryGetProperty("model_name", out var mn) ? mn.GetString() ?? "" : "";
            var csiCode = element.TryGetProperty("csi_code", out var cc) ? cc.GetString() : null;
            var description = element.TryGetProperty("description", out var desc) ? desc.GetString() : null;
            var score = element.TryGetProperty("score", out var sc) ? sc.GetSingle() : 0.5f;
            var reasoning = element.TryGetProperty("reasoning", out var r) ? r.GetString() : null;

            List<string>? useCases = null;
            if (element.TryGetProperty("use_cases", out var uc) && uc.ValueKind == JsonValueKind.Array)
            {
                useCases = [];
                foreach (var u in uc.EnumerateArray())
                {
                    var text = u.GetString();
                    if (!string.IsNullOrEmpty(text))
                        useCases.Add(text);
                }
            }

            Dictionary<string, object>? specs = null;
            if (element.TryGetProperty("specifications", out var sp) && sp.ValueKind == JsonValueKind.Object)
            {
                specs = JsonSerializer.Deserialize<Dictionary<string, object>>(sp.GetRawText());
            }

            return new ProductMatch
            {
                ProductId = productId,
                Vendor = vendor,
                ModelName = modelName,
                CsiCode = csiCode,
                Description = description,
                UseCases = useCases,
                TechnicalSpecs = specs,
                FinalScore = score,
                Reasoning = reasoning
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse individual product match from agent response");
            return null;
        }
    }

    /// <summary>
    /// Extract JSON from the agent's response, stripping markdown code fences if present.
    /// </summary>
    private static string ExtractJson(string text)
    {
        var trimmed = text.Trim();

        // Strip Nova Pro <thinking>...</thinking> blocks
        trimmed = Regex.Replace(trimmed, @"<thinking>[\s\S]*?</thinking>", "", RegexOptions.IgnoreCase).Trim();

        // Strip markdown JSON fence
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (end > 7)
                return trimmed[7..end].Trim();
        }

        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline > 0 && end > firstNewline)
                return trimmed[(firstNewline + 1)..end].Trim();
        }

        // Find first { and last } for bare JSON
        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
            return trimmed[firstBrace..(lastBrace + 1)];

        return trimmed;
    }

    private static string LoadSystemPrompt()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "SourcingEngine.Core.Prompts.AgentSystemPrompt.txt";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found. Ensure the file exists and is set as EmbeddedResource.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
