using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Shared utility methods for embedding operations.
/// Consolidates duplicate code from OllamaEmbeddingService and LocalEmbeddingService.
/// </summary>
public static class EmbeddingUtilities
{
    /// <summary>
    /// Compute a short SHA-256 hash of the input text (lowercased, trimmed).
    /// Used as a cache key for embedding results.
    /// </summary>
    public static string ComputeHash(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input.ToLowerInvariant().Trim());
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash)[..16];
    }

    /// <summary>
    /// Truncate text for logging purposes, appending "..." if truncated.
    /// </summary>
    public static string TruncateForLog(string text, int maxLength = 50)
    {
        if (text.Length <= maxLength) return text;
        return text[..maxLength] + "...";
    }

    /// <summary>
    /// Format a float[] embedding as a PostgreSQL vector string literal: '[0.1,0.2,0.3,...]'
    /// Compatible with pgvector's ::vector cast.
    /// </summary>
    public static string FormatPgVector(float[] embedding)
    {
        return "[" + string.Join(",", embedding.Select(f => f.ToString("G9", CultureInfo.InvariantCulture))) + "]";
    }
}
