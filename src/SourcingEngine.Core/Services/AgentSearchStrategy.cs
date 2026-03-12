using System.Diagnostics;
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

    private const int MaxRetries = 3;
    private static readonly int[] RetryDelaysMs = [5_000, 15_000, 30_000];

    /// <summary>
    /// Tracks when the last throttling event occurred so we can pace
    /// sequential items and let the Bedrock TPM bucket recover.
    /// </summary>
    private DateTime _lastThrottleTime = DateTime.MinValue;
    private const int PostThrottleCooldownMs = 10_000;

    public async Task<SearchStrategyResult> ExecuteAsync(
        BomLineItem item, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        _logger.LogInformation(
            "AgentSearchStrategy: Searching for BOM item '{BomItem}' - '{Description}'",
            item.BomItem, item.Description);

        // If we recently hit throttling, wait before starting this item
        var sinceLast = DateTime.UtcNow - _lastThrottleTime;
        if (sinceLast.TotalMilliseconds < PostThrottleCooldownMs)
        {
            var cooldown = PostThrottleCooldownMs - (int)sinceLast.TotalMilliseconds;
            _logger.LogInformation(
                "Cooling down {CooldownMs}ms before searching '{BomItem}' (recent throttling)",
                cooldown, item.BomItem);
            await Task.Delay(cooldown, cancellationToken);
        }

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                // Build the agent fresh on each attempt (clean chat history)
                var kernel = CreateKernel();
                var agent = CreateAgent(kernel);

                // Build user message from BOM item
                var userMessage = BuildUserMessage(item);
                if (attempt == 0)
                    _logger.LogDebug("Agent user message: {Message}", userMessage);

                // Run the agent and collect the final response
                var responseText = await InvokeAgentAsync(agent, userMessage, cancellationToken);

                // Parse the agent's structured response
                return ParseAgentResponse(responseText, item);
            }
            catch (OperationCanceledException)
            {
                // Re-throw cancellation so the orchestrator's per-item timeout handler
                // can report it properly. Don't swallow it as a generic error.
                _logger.LogWarning(
                    "Agent search cancelled for '{BomItem}' after {ElapsedMs}ms (attempt {Attempt})",
                    item.BomItem, sw.ElapsedMilliseconds, attempt + 1);
                throw;
            }
            catch (Exception ex) when (IsThrottlingException(ex) && attempt < MaxRetries)
            {
                _lastThrottleTime = DateTime.UtcNow;
                var delay = RetryDelaysMs[attempt];
                _logger.LogWarning(
                    "Bedrock throttling on attempt {Attempt}/{Max} for '{BomItem}' at {ElapsedMs}ms, retrying in {Delay}ms",
                    attempt + 1, MaxRetries, item.BomItem, sw.ElapsedMilliseconds, delay);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                if (IsThrottlingException(ex))
                    _lastThrottleTime = DateTime.UtcNow;

                _logger.LogError(ex,
                    "Agent search failed for BOM item '{BomItem}' after {ElapsedMs}ms",
                    item.BomItem, sw.ElapsedMilliseconds);
                return new SearchStrategyResult
                {
                    Matches = [],
                    Warnings = [$"Agent search failed: {ex.Message}"],
                    FamilyLabel = null,
                    CsiCode = null
                };
            }
        }

        // Should not reach here, but safety fallback
        return new SearchStrategyResult
        {
            Matches = [],
            Warnings = ["Agent search exhausted all retry attempts due to throttling"],
            FamilyLabel = null,
            CsiCode = null
        };
    }

    private static bool IsThrottlingException(Exception ex)
    {
        // Check the exception chain for Bedrock ThrottlingException
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current.GetType().Name == "ThrottlingException" ||
                current.Message.Contains("throttl", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("Too many tokens", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private Kernel CreateKernel()
    {
        var builder = Kernel.CreateBuilder();

        // Add Bedrock chat completion via Converse API
        // Use AddBedrockChatClient (IChatClient-based) instead of AddBedrockChatCompletionService
        // because the latter's internal factory doesn't route amazon.nova-* models.
        // Explicit timeout ensures individual Bedrock API calls don't hang indefinitely.
        var bedrockRuntime = new AmazonBedrockRuntimeClient(new AmazonBedrockRuntimeConfig
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(_settings.Region),
            Timeout = TimeSpan.FromSeconds(120),
        });

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
        sb.AppendLine("Search the database for ALL products matching this BOM line item. Maximize recall — find every possible match across all related material families.");
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

        sb.AppendLine();
        sb.AppendLine("Follow the 6-phase search strategy. Always query product_attribute_values for matched products. Report all requirement gaps.");

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
        // The agent should return pure JSON, but might wrap in markdown fences.
        // ExtractJson also handles truncated responses from token-limit cutoffs.
        var json = ExtractJson(responseText);

        if (json != responseText.Trim())
            _logger.LogDebug("Agent response required JSON extraction/repair (original {OrigLen} chars → {JsonLen} chars)",
                responseText.Length, json.Length);

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
    /// If the JSON is truncated (due to token limits), attempts to repair it by closing
    /// open brackets/braces so that already-complete matches can still be parsed.
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
                trimmed = trimmed[7..end].Trim();
            else
                trimmed = trimmed[7..].Trim(); // No closing fence — truncated
        }
        else if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline > 0 && end > firstNewline)
                trimmed = trimmed[(firstNewline + 1)..end].Trim();
            else if (firstNewline > 0)
                trimmed = trimmed[(firstNewline + 1)..].Trim();
        }

        // Find first { for bare JSON
        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
            trimmed = trimmed[firstBrace..(lastBrace + 1)];
        else if (firstBrace >= 0)
            trimmed = trimmed[firstBrace..]; // No closing brace — truncated

        // Try parsing as-is first; only attempt repair if it fails
        try
        {
            using var _ = JsonDocument.Parse(trimmed);
            return trimmed;
        }
        catch (JsonException)
        {
            // Fall through to repair
        }

        return RepairTruncatedJson(trimmed);
    }

    /// <summary>
    /// Attempt to repair truncated JSON by removing the last incomplete element
    /// and closing any open arrays/objects. This salvages already-complete matches
    /// when the LLM's response is cut off by the token limit.
    /// </summary>
    private static string RepairTruncatedJson(string json)
    {
        // Strategy: find the last complete object boundary in the matches array,
        // truncate there, then close any remaining open brackets/braces.

        // Find the last position where a complete JSON object ended ("},")
        // or where an array element ended cleanly
        var lastCompleteObject = -1;
        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            if (escaped) { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            switch (c)
            {
                case '{': case '[': depth++; break;
                case '}':
                    depth--;
                    if (depth >= 1) // Completed an inner object while still inside the root
                        lastCompleteObject = i;
                    break;
                case ']':
                    depth--;
                    if (depth >= 1)
                        lastCompleteObject = i;
                    break;
            }
        }

        if (lastCompleteObject <= 0)
            return json; // Can't salvage

        // Truncate after the last complete inner object
        var repaired = json[..(lastCompleteObject + 1)];

        // Re-scan to determine what closers are needed
        inString = false;
        escaped = false;
        var openStack = new Stack<char>();

        for (int i = 0; i < repaired.Length; i++)
        {
            char c = repaired[i];
            if (escaped) { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            switch (c)
            {
                case '{': openStack.Push('}'); break;
                case '[': openStack.Push(']'); break;
                case '}': case ']':
                    if (openStack.Count > 0) openStack.Pop();
                    break;
            }
        }

        // Close all remaining open brackets/braces
        var sb = new StringBuilder(repaired);
        while (openStack.Count > 0)
            sb.Append(openStack.Pop());

        return sb.ToString();
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
