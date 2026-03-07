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
    /// Supports any model available via Bedrock Converse API.
    /// Examples:
    ///   - us.amazon.nova-pro-v1:0 (best reasoning, supports tool use)
    ///   - us.amazon.nova-lite-v1:0 (cheapest)
    ///   - anthropic.claude-sonnet-4-20250514-v1:0 (Anthropic alternative)
    /// </summary>
    public string ModelId { get; set; } = "us.amazon.nova-pro-v1:0";

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
    /// Safety guard against runaway reasoning loops.
    /// </summary>
    public int MaxToolCalls { get; set; } = 10;

    /// <summary>
    /// Temperature for the agent's LLM reasoning (0.0-1.0).
    /// Lower = more deterministic SQL generation.
    /// </summary>
    public float Temperature { get; set; } = 0.1f;

    /// <summary>
    /// Maximum tokens for the agent's response.
    /// </summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>
    /// Maximum number of product matches the agent should return per BOM item.
    /// </summary>
    public int MaxResults { get; set; } = 10;
}
