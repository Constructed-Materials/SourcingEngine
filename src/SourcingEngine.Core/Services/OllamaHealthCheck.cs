namespace SourcingEngine.Core.Services;

/// <summary>
/// Shared health check utilities for Ollama API.
/// Consolidates duplicate health check logic from OllamaEmbeddingService,
/// OllamaQueryParserService, and test fixtures.
/// </summary>
public static class OllamaHealthCheck
{
    /// <summary>
    /// Quick check: can we reach the Ollama API?
    /// </summary>
    public static async Task<bool> IsAvailableAsync(string baseUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = await httpClient.GetAsync($"{baseUrl.TrimEnd('/')}/api/tags", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Check if a specific model is available in Ollama.
    /// </summary>
    public static async Task<bool> IsModelAvailableAsync(string baseUrl, string modelName, CancellationToken cancellationToken = default)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = await httpClient.GetAsync($"{baseUrl.TrimEnd('/')}/api/tags", cancellationToken);
            if (!response.IsSuccessStatusCode) return false;

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return content.Contains(modelName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Check if a specific model is available using an existing HttpClient.
    /// </summary>
    public static async Task<bool> IsModelAvailableAsync(HttpClient httpClient, string modelName, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.GetAsync("/api/tags", cancellationToken);
            if (!response.IsSuccessStatusCode) return false;

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return content.Contains(modelName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
