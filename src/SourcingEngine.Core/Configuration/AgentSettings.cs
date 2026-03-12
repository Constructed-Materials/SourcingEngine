namespace SourcingEngine.Core.Configuration;

/// <summary>
/// Configuration settings for the AI agent-based search strategy.
/// The agent uses a Bedrock LLM to reason about BOM items and queries
/// the Supabase database via MCP to find matching products.
/// </summary>
public class AgentSettings
{
    public const string SectionName = "Agent";

    /// <summary>
    /// Whether the agent search strategy is enabled.
    /// When true, <see cref="AgentSearchStrategy"/> is registered as <see cref="ISearchStrategy"/>
    /// instead of <see cref="ProductFirstStrategy"/>.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Bedrock model ID for the agent's reasoning (the "brain").
    /// Uses global cross-region inference for highest throughput and auto-routing.
    /// Examples:
    ///   - global.anthropic.claude-sonnet-4-6 (recommended — global routing, highest TPM)
    ///   - us.anthropic.claude-sonnet-4-20250514-v1:0 (US-only, lower TPM)
    ///   - us.amazon.nova-pro-v1:0 (Amazon alternative, weaker at nuanced SQL)
    /// </summary>
    public string ModelId { get; set; } = "global.anthropic.claude-sonnet-4-6";

    /// <summary>
    /// AWS region for the agent's Bedrock model.
    /// Defaults to the same region as the embedding/parsing models.
    /// </summary>
    public string Region { get; set; } = "us-east-2";

    /// <summary>
    /// Supabase MCP endpoint URL for database access.
    /// The agent calls execute_sql through this endpoint.
    /// </summary>
    public string SupabaseMcpUrl { get; set; } = string.Empty;

    /// <summary>
    /// Bearer token for authenticating with the Supabase MCP endpoint.
    /// </summary>
    public string SupabaseMcpAuthToken { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of tool calls the agent can make per BOM item search.
    /// The 6-phase search strategy typically uses 3-5 queries, plus error recovery.
    /// Safety guard against runaway reasoning loops.
    /// </summary>
    public int MaxToolCalls { get; set; } = 15;

    /// <summary>
    /// Temperature for the agent's LLM reasoning (0.0-1.0).
    /// Lower = more deterministic SQL generation.
    /// </summary>
    public float Temperature { get; set; } = 0.1f;

    /// <summary>
    /// Maximum tokens for the agent's response.
    /// Claude 3.7+ models reserve (input + max_tokens) upfront from the TPM quota,
    /// so keep this as low as practical. 8192 covers ~15 matches with reasoning.
    /// </summary>
    public int MaxTokens { get; set; } = 8192;

    /// <summary>
    /// Maximum number of product matches the agent should return per BOM item.
    /// </summary>
    public int MaxResults { get; set; } = 15;

    /// <summary>
    /// Maximum number of BOM items to search concurrently.
    /// Higher values reduce total latency but increase Bedrock throttling risk.
    /// Set to 1 for sequential processing.
    /// </summary>
    public int MaxConcurrentSearches { get; set; } = 1;

    /// <summary>
    /// Per-item timeout in seconds for a single agent search.
    /// Agent conversations involve 3-5 Bedrock calls + 3-5 MCP calls.
    /// 240s is generous enough for the full conversation while fitting
    /// 3 items within the 900s Lambda timeout budget (3 × 240 = 720s).
    /// </summary>
    public int PerItemTimeoutSeconds { get; set; } = 240;
}
