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

    /// <summary>
    /// Compute cosine similarity between two embedding vectors.
    /// For normalized vectors (norm = 1.0) this is equivalent to the dot product.
    /// Returns a value in the range [-1.0, 1.0], where 1.0 means identical direction.
    /// </summary>
    /// <param name="a">First embedding vector</param>
    /// <param name="b">Second embedding vector (must have same length as <paramref name="a"/>)</param>
    /// <returns>Cosine similarity score</returns>
    /// <exception cref="ArgumentException">When vectors have different lengths</exception>
    public static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException($"Vector dimensions must match: {a.Length} vs {b.Length}");

        // For normalized vectors (Titan V2 with EmbeddingNormalize: true),
        // cosine similarity = dot product. We compute the full formula for safety.
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * (double)b[i];
            normA += a[i] * (double)a[i];
            normB += b[i] * (double)b[i];
        }

        var denominator = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denominator < 1e-10 ? 0f : (float)(dot / denominator);
    }
}
