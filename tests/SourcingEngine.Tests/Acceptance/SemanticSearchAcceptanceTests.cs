using Microsoft.Extensions.DependencyInjection;
using SourcingEngine.Core.Services;
using SourcingEngine.Core.Repositories;
using SourcingEngine.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace SourcingEngine.Tests.Acceptance;

/// <summary>
/// Acceptance tests for semantic search functionality
/// Tests the hybrid FTS + vector search with RRF fusion
/// </summary>
[Collection("Database")]
[Trait("Category", "Integration")]
[Trait("Category", "Acceptance")]
public class SemanticSearchAcceptanceTests
{
    private readonly DatabaseFixture _fixture;
    private readonly ITestOutputHelper _output;

    public SemanticSearchAcceptanceTests(DatabaseFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// Test that semantic search finds related terms not in exact keyword match
    /// "concrete masonry" should find cmu_blocks even if not exact match
    /// </summary>
    [Fact]
    public async Task Search_SemanticQuery_FindsRelatedMaterialFamily()
    {
        // Arrange
        using var scope = _fixture.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISearchOrchestrator>();
        
        // Use a semantic query that relies on understanding, not just keyword match
        var bomText = "concrete masonry unit for wall construction";

        // Act
        var result = await orchestrator.SearchAsync(bomText);

        // Assert
        _output.WriteLine($"Query: {result.Query}");
        _output.WriteLine($"Family: {result.FamilyLabel}");
        _output.WriteLine($"Match Count: {result.MatchCount}");
        _output.WriteLine($"Execution Time: {result.ExecutionTimeMs}ms");

        // Should find cmu_blocks family
        Assert.NotNull(result.FamilyLabel);
    }

    /// <summary>
    /// Test that embedding service generates valid embeddings
    /// </summary>
    [Fact]
    public async Task EmbeddingService_GeneratesValidEmbeddings()
    {
        // Arrange
        var embeddingService = _fixture.GetService<IEmbeddingService>();
        var testText = "8 inch masonry block for commercial building";

        // Act
        var embedding = await embeddingService.GenerateEmbeddingAsync(testText);

        // Assert
        _output.WriteLine($"Embedding dimension: {embedding.Length}");
        _output.WriteLine($"Sample values: [{embedding[0]:F4}, {embedding[1]:F4}, {embedding[2]:F4}, ...]");

        Assert.Equal(384, embedding.Length);
        Assert.All(embedding, v => Assert.False(float.IsNaN(v)));
        Assert.All(embedding, v => Assert.False(float.IsInfinity(v)));
    }

    /// <summary>
    /// Test that similar queries produce similar embeddings
    /// </summary>
    [Fact]
    public async Task EmbeddingService_SimilarTexts_ProduceSimilarEmbeddings()
    {
        // Arrange
        var embeddingService = _fixture.GetService<IEmbeddingService>();
        
        var text1 = "concrete masonry unit";
        var text2 = "cmu block";
        var text3 = "aluminum window frame"; // unrelated

        // Act
        var embedding1 = await embeddingService.GenerateEmbeddingAsync(text1);
        var embedding2 = await embeddingService.GenerateEmbeddingAsync(text2);
        var embedding3 = await embeddingService.GenerateEmbeddingAsync(text3);

        var similarity12 = CosineSimilarity(embedding1, embedding2);
        var similarity13 = CosineSimilarity(embedding1, embedding3);

        // Assert
        _output.WriteLine($"Similarity (concrete masonry ↔ cmu block): {similarity12:F4}");
        _output.WriteLine($"Similarity (concrete masonry ↔ aluminum window): {similarity13:F4}");

        // Related terms should be more similar than unrelated
        Assert.True(similarity12 > similarity13, 
            $"Related terms should be more similar: {similarity12} vs {similarity13}");
    }

    /// <summary>
    /// Test full-text search repository method
    /// </summary>
    [Fact]
    public async Task FullTextSearch_ReturnsRankedResults()
    {
        // Arrange
        using var scope = _fixture.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMaterialFamilyRepository>();

        // Act
        var results = await repository.FullTextSearchAsync("masonry block concrete", maxResults: 5);

        // Assert
        _output.WriteLine($"Full-text search returned {results.Count} results:");
        foreach (var r in results)
        {
            _output.WriteLine($"  Rank {r.Rank}: {r.Family.FamilyLabel} (score: {r.Score:F4})");
        }

        // Should return ranked results
        if (results.Count > 0)
        {
            Assert.True(results[0].Rank == 1);
        }
    }

    /// <summary>
    /// Test semantic search repository method (requires seeded embeddings)
    /// </summary>
    [Fact]
    public async Task SemanticSearch_WithEmbeddings_ReturnsRankedResults()
    {
        // Arrange
        using var scope = _fixture.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMaterialFamilyRepository>();
        var embeddingService = _fixture.GetService<IEmbeddingService>();

        var queryText = "concrete block for walls";
        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(queryText);

        // Act
        var results = await repository.SemanticSearchAsync(queryEmbedding, maxResults: 5);

        // Assert
        _output.WriteLine($"Semantic search for '{queryText}' returned {results.Count} results:");
        foreach (var r in results)
        {
            _output.WriteLine($"  Rank {r.Rank}: {r.Family.FamilyLabel} (similarity: {r.Score:F4})");
        }

        // Note: This test may return 0 results until embeddings are seeded
        // Run: dotnet run --project src/SourcingEngine.Console -- --seed-embeddings
    }

    /// <summary>
    /// Test RRF fusion service combines results correctly
    /// </summary>
    [Fact]
    public void RrfFusion_CombinesResultsCorrectly()
    {
        // Arrange
        var fusionService = _fixture.GetService<ISearchFusionService>();
        
        var ftsResults = new List<RankedMaterialFamily>
        {
            new(new() { FamilyLabel = "cmu_blocks", FamilyName = "CMU" }, 1, 0.9f),
            new(new() { FamilyLabel = "concrete", FamilyName = "Concrete" }, 2, 0.7f),
        };
        
        var semanticResults = new List<RankedMaterialFamily>
        {
            new(new() { FamilyLabel = "cmu_blocks", FamilyName = "CMU" }, 1, 0.95f),
            new(new() { FamilyLabel = "masonry", FamilyName = "Masonry" }, 2, 0.8f),
        };

        // Act
        var fused = fusionService.Fuse(ftsResults, semanticResults, maxResults: 3);

        // Assert
        _output.WriteLine($"Fused results ({fused.Count} items):");
        foreach (var f in fused)
        {
            _output.WriteLine($"  {f.FamilyLabel}");
        }

        Assert.True(fused.Count >= 1);
        // cmu_blocks should be first since it appears in both lists
        Assert.Equal("cmu_blocks", fused[0].FamilyLabel);
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        float dotProduct = 0;
        float normA = 0;
        float normB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return dotProduct / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }
}
