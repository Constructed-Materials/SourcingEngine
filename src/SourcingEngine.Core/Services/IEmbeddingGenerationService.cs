using SourcingEngine.Core.Models;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Options for product embedding generation.
/// </summary>
public class EmbeddingGenerationOptions
{
    /// <summary>Generate for all products.</summary>
    public bool GenerateAll { get; init; }
    
    /// <summary>Generate only for products missing embeddings.</summary>
    public bool MissingOnly { get; init; }
    
    /// <summary>Generate for a specific product by ID.</summary>
    public Guid? SpecificProductId { get; init; }
    
    /// <summary>Generate for all products of a specific family.</summary>
    public string? FamilyLabel { get; init; }
}

/// <summary>
/// Result of an embedding generation run.
/// </summary>
public record EmbeddingGenerationResult(int Processed, int Failed, int Total);

/// <summary>
/// Service responsible for generating and storing embeddings for products and material families.
/// Extracted from Program.cs to follow Single Responsibility Principle.
/// </summary>
public interface IEmbeddingGenerationService
{
    /// <summary>
    /// Generate embeddings for products based on the given options.
    /// </summary>
    Task<EmbeddingGenerationResult> GenerateProductEmbeddingsAsync(
        EmbeddingGenerationOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Seed embeddings for all material families.
    /// </summary>
    Task<EmbeddingGenerationResult> SeedFamilyEmbeddingsAsync(
        CancellationToken cancellationToken = default);
}
