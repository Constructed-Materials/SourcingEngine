using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Semantic Kernel plugin that exposes Supabase MCP's execute_sql tool
/// to the agent. The agent calls this to query the SourcingEngine database.
/// 
/// Implements MCP Streamable HTTP transport: sends an initialize handshake
/// to obtain a session ID, then passes it on every tools/call request.
/// </summary>
public class SupabaseMcpPlugin
{
    private readonly HttpClient _httpClient;
    private readonly string _mcpUrl;
    private readonly string _authToken;
    private readonly ILogger<SupabaseMcpPlugin> _logger;

    private string? _sessionId;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private int _nextRequestId = 1;

    public SupabaseMcpPlugin(HttpClient httpClient, string mcpUrl, string authToken, ILogger<SupabaseMcpPlugin> logger)
    {
        _httpClient = httpClient;
        _mcpUrl = mcpUrl;
        _authToken = authToken;
        _logger = logger;
    }

    /// <summary>
    /// Execute a read-only SQL query against the SourcingEngine Supabase PostgreSQL database.
    /// Use this to search for products, materials, vendors, and product knowledge.
    /// 
    /// Available tables (all in public schema):
    /// - products: product_id (uuid PK), vendor_id (int FK), family_label (varchar), model_name (varchar), csi_section_code (varchar), is_active (bool), base_price (numeric)
    /// - product_knowledge: product_id (uuid FK), model (varchar), vendor_key (varchar), description (text), use_cases (text[]), specifications (jsonb), ideal_applications (text[]), not_recommended_for (text[])
    /// - vendors: vendor_id (int PK), name (varchar), vendor_type (varchar), is_manufacturer (bool), certifications (text[]), description (text)
    /// - cm_master_materials: family_label (varchar PK), family_name (varchar), csi_division (varchar), synonyms (text), fts (tsvector)
    /// - product_certifications: product_id (uuid FK), cert_id (uuid FK), verification_status (varchar)
    /// - certifications: cert_id (uuid PK), code (varchar), title (text), issuer (varchar)
    /// - product_attribute_values: pav_id (uuid PK), product_id (uuid FK), attribute_key (varchar), value_text (text), value_num (numeric), value_unit (varchar)
    /// 
    /// IMPORTANT: Only use SELECT queries. Never INSERT, UPDATE, DELETE, or DROP.
    /// IMPORTANT: Always filter by is_active = true on the products table.
    /// </summary>
    /// <param name="query">A read-only SQL SELECT query to execute against the database.</param>
    /// <returns>JSON array of result rows, or an error message.</returns>
    [KernelFunction("execute_sql")]
    [Description("Execute a read-only SQL query against the SourcingEngine PostgreSQL database to search for products, materials, vendors, and knowledge.")]
    public async Task<string> ExecuteSqlAsync(
        [Description("A read-only SQL SELECT query. Only SELECT is allowed. Always filter products by is_active = true.")] 
        string query)
    {
        _logger.LogInformation("Agent SQL query: {Query}", query);

        // Sanitize: strip markdown code fences and leading prose the LLM may wrap around the SQL
        query = StripMarkdownFences(query);

        // Safety: reject non-SELECT queries
        var trimmed = query.TrimStart().ToUpperInvariant();
        if (!trimmed.StartsWith("SELECT") && !trimmed.StartsWith("WITH"))
        {
            _logger.LogWarning("Agent attempted non-SELECT query: {Query}", query);
            return JsonSerializer.Serialize(new { error = "Only SELECT queries are allowed." });
        }

        try
        {
            await EnsureSessionAsync();

            var requestId = Interlocked.Increment(ref _nextRequestId);
            var requestBody = new
            {
                jsonrpc = "2.0",
                id = requestId,
                method = "tools/call",
                @params = new
                {
                    name = "execute_sql",
                    arguments = new { query }
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var request = CreateMcpRequest(json);
            request.Headers.Add("Mcp-Session-Id", _sessionId);

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Supabase MCP returned {StatusCode}: {Body}",
                    response.StatusCode, responseBody[..Math.Min(responseBody.Length, 500)]);

                // If session expired, clear it so next call re-initializes
                if ((int)response.StatusCode == 400 || (int)response.StatusCode == 404)
                {
                    _sessionId = null;
                }

                return JsonSerializer.Serialize(new { error = $"MCP error: {response.StatusCode}", details = responseBody[..Math.Min(responseBody.Length, 500)] });
            }

            _logger.LogDebug("Supabase MCP response: {Response}", responseBody[..Math.Min(responseBody.Length, 500)]);

            return ExtractSqlResult(responseBody);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute SQL via Supabase MCP");
            return JsonSerializer.Serialize(new { error = $"SQL execution failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Initializes the MCP session if not already done. Thread-safe.
    /// </summary>
    private async Task EnsureSessionAsync()
    {
        if (_sessionId is not null) return;

        await _initLock.WaitAsync();
        try
        {
            if (_sessionId is not null) return;

            _logger.LogInformation("Initializing MCP session with {Url}", _mcpUrl);

            var initBody = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "SourcingEngine", version = "1.0" }
                }
            };

            var json = JsonSerializer.Serialize(initBody);
            var request = CreateMcpRequest(json);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    $"MCP initialize failed with {response.StatusCode}: {body[..Math.Min(body.Length, 500)]}");
            }

            // Extract session ID from response header
            if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessionValues))
            {
                _sessionId = sessionValues.First();
                _logger.LogInformation("MCP session established: {SessionId}", _sessionId[..Math.Min(_sessionId.Length, 20)] + "...");
            }
            else
            {
                throw new InvalidOperationException("MCP initialize response missing Mcp-Session-Id header");
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Creates an HTTP request with the required MCP headers.
    /// </summary>
    private HttpRequestMessage CreateMcpRequest(string jsonBody)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _mcpUrl)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (!string.IsNullOrEmpty(_authToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authToken);
        }

        return request;
    }

    /// <summary>
    /// Extracts the SQL result from the MCP JSON-RPC response.
    /// The response structure is: result.content[0].text contains a wrapper with
    /// untrusted-data boundaries around the actual JSON array of rows.
    /// </summary>
    internal static string ExtractSqlResult(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        // Check for JSON-RPC error
        if (root.TryGetProperty("error", out var errorEl))
        {
            return errorEl.GetRawText();
        }

        if (!root.TryGetProperty("result", out var result))
            return responseBody;

        // MCP result format: { content: [{ type: "text", text: "..." }] }
        if (!result.TryGetProperty("content", out var content) || content.GetArrayLength() == 0)
            return result.GetRawText();

        var textValue = content[0].GetProperty("text").GetString() ?? "";

        // The text contains a JSON object with a "result" field that has untrusted-data boundaries.
        // Try to extract the inner result.
        // Pattern: {"result":"...<untrusted-data-UUID>\n[...actual JSON...]\n</untrusted-data-UUID>..."}
        if (textValue.StartsWith("{"))
        {
            try
            {
                using var innerDoc = JsonDocument.Parse(textValue);
                if (innerDoc.RootElement.TryGetProperty("result", out var innerResult))
                {
                    var innerText = innerResult.GetString() ?? "";
                    // Extract JSON between untrusted-data tags
                    var match = Regex.Match(innerText, @"<untrusted-data[^>]*>\s*([\s\S]*?)\s*</untrusted-data[^>]*>");
                    if (match.Success)
                    {
                        return match.Groups[1].Value.Trim();
                    }
                    return innerText;
                }
            }
            catch (JsonException)
            {
                // Not valid JSON, fall through
            }
        }

        return textValue;
    }

    /// <summary>
    /// Strips markdown code fences and SQL-prefixed prose that LLMs sometimes wrap around queries.
    /// e.g., "```sql\nSELECT ...\n```" → "SELECT ..."
    /// </summary>
    internal static string StripMarkdownFences(string sql)
    {
        var trimmed = sql.Trim();

        // Strip ```sql ... ``` or ``` ... ```
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline > 0 && lastFence > firstNewline)
                return trimmed[(firstNewline + 1)..lastFence].Trim();
            if (firstNewline > 0)
                return trimmed[(firstNewline + 1)..].Trim();
        }

        return trimmed;
    }
}
