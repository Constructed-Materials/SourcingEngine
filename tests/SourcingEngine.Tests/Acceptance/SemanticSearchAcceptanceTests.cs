using Microsoft.Extensions.DependencyInjection;
using SourcingEngine.Core.Services;
using SourcingEngine.Core.Repositories;
using SourcingEngine.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace SourcingEngine.Tests.Acceptance;

/// <summary>
/// Acceptance tests for semantic search functionality.
/// Tests Bedrock-powered vector search with product embeddings.
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
    /// Test that semantic search finds related terms not in exact keyword match.
    /// "concrete masonry" should find cmu_blocks via embedding similarity.
    /// </summary>
    [Fact]
    public async Task Search_SemanticQuery_FindsRelatedMaterialFamily()
    {
        using var scope = _fixture.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISearchOrchestrator>();
        
        var bomText = "concrete masonry unit for wall construction";

        var result = await orchestrator.SearchAsync(bomText);

        _output.WriteLine($"Query: {result.Query}");
        _output.WriteLine($"Family: {result.FamilyLabel}");
        _output.WriteLine($"Match Count: {result.MatchCount}");
        _output.WriteLine($"Execution Time: {result.ExecutionTimeMs}ms");

        Assert.NotNull(result.FamilyLabel);
    }

    /// <summary>
    /// Test that embedding service generates valid embeddings.
    /// </summary>
    [Fact]
    public async Task EmbeddingService_GeneratesValidEmbeddings()
    {
        var embeddingService = _fixture.GetService<IEmbeddingService>();
        var testText = "8 inch masonry block for commercial building";

        var embedding = await embeddingService.GenerateEmbeddingAsync(testText);

        _output.WriteLine($"Embedding dimension: {embedding.Length}");
        _output.WriteLine($"Embedding provider: {embeddingService.GetType().Name}");
        _output.WriteLine($"Sample values: [{embedding[0]:F4}, {embedding[1]:F4}, {embedding[2]:F4}, ...]");

        Assert.Equal(embeddingService.EmbeddingDimension, embedding.Length);
        Assert.All(embedding, v => Assert.False(float.IsNaN(v)));
        Assert.All(embedding, v => Assert.False(float.IsInfinity(v)));
    }

    /// <summary>
    /// Test that similar queries produce similar embeddings.
    /// </summary>
    [Fact]
    public async Task EmbeddingService_SimilarTexts_ProduceSimilarEmbeddings()
    {
        var embeddingService = _fixture.GetService<IEmbeddingService>();
        
        var text1 = "concrete masonry unit";
        var text2 = "cmu block";
        var text3 = "aluminum window frame"; // unrelated

        var embedding1 = await embeddingService.GenerateEmbeddingAsync(text1);
        var embedding2 = await embeddingService.GenerateEmbeddingAsync(text2);
        var embedding3 = await embeddingService.GenerateEmbeddingAsync(text3);

        var similarity12 = CosineSimilarity(embedding1, embedding2);
        var similarity13 = CosineSimilarity(embedding1, embedding3);

        _output.WriteLine($"Similarity (concrete masonry ↔ cmu block): {similarity12:F4}");
        _output.WriteLine($"Similarity (concrete masonry ↔ aluminum window): {similarity13:F4}");

        Assert.True(similarity12 > similarity13, 
            $"Related terms should be more similar: {similarity12} vs {similarity13}");
    }

    /// <summary>
    /// Test full-text search repository method.
    /// </summary>
    [Fact]
    public async Task FullTextSearch_ReturnsRankedResults()
    {
        using var scope = _fixture.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMaterialFamilyRepository>();

        var results = await repository.FullTextSearchAsync("masonry block concrete", maxResults: 5);

        _output.WriteLine($"Full-text search returned {results.Count} results:");
        foreach (var r in results)
        {
            _output.WriteLine($"  Rank {r.Rank}: {r.Family.FamilyLabel} (score: {r.Score:F4})");
        }

        Assert.True(results.Count >= 1,
            "Expected ≥1 full-text search result for 'masonry block concrete'");
        Assert.True(results[0].Rank == 1);
    }

    /// <summary>
    /// Test semantic search repository method (requires seeded embeddings).
    /// </summary>
    [Fact]
    public async Task SemanticSearch_WithEmbeddings_ReturnsRankedResults()
    {
        using var scope = _fixture.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMaterialFamilyRepository>();
        var embeddingService = _fixture.GetService<IEmbeddingService>();

        var queryText = "concrete block for walls";
        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(queryText);

        var results = await repository.SemanticSearchAsync(queryEmbedding, maxResults: 5);

        _output.WriteLine($"Semantic search for '{queryText}' returned {results.Count} results:");
        foreach (var r in results)
        {
            _output.WriteLine($"  Rank {r.Rank}: {r.Family.FamilyLabel} (similarity: {r.Score:F4})");
        }

        Assert.True(results.Count >= 1,
            $"Expected ≥1 semantic search result for '{queryText}'. " +
            "Ensure family embeddings are seeded (--seed-embeddings) with the same embedding model.");
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

/// <summary>
/// Acceptance tests for product-level semantic search.
/// Requires Bedrock and product embeddings generated.
/// </summary>
[Collection("Database")]
[Trait("Category", "Integration")]
[Trait("Category", "SemanticProducts")]
public class SemanticProductSearchTests
{
    private readonly DatabaseFixture _fixture;
    private readonly ITestOutputHelper _output;

    public SemanticProductSearchTests(DatabaseFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// Test ProductFirst search returns semantic matches.
    /// </summary>
    [Fact]
    public async Task Search_ProductFirst_ReturnsSemanticMatches()
    {
        using var scope = _fixture.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISearchOrchestrator>();

        var bomText = "aluminum window frame for commercial building";

        var result = await orchestrator.SearchAsync(bomText);

        _output.WriteLine($"Query: {result.Query}");
        _output.WriteLine($"Family: {result.FamilyLabel}");
        _output.WriteLine($"CSI: {result.CsiCode}");
        _output.WriteLine($"Execution Time: {result.ExecutionTimeMs}ms");
        _output.WriteLine($"Matches: {result.MatchCount}");

        foreach (var match in result.Matches.Take(5))
        {
            _output.WriteLine($"  {match.Vendor} - {match.ModelName} [CSI:{match.CsiCode}] (score: {match.SemanticScore:F4})");
            _output.WriteLine($"    UseWhen: {match.UseWhen ?? "(none)"}");
            _output.WriteLine($"    Schema: {match.SourceSchema ?? "(none)"}");
        }

        Assert.True(result.MatchCount >= 1,
            $"Expected ≥1 semantic match for '{bomText}'. " +
            "Ensure product embeddings are generated (--generate-embeddings --all).");
        Assert.All(result.Matches, m =>
        {
            Assert.NotNull(m.Vendor);
            Assert.NotNull(m.ModelName);
            Assert.True(m.SemanticScore > 0, "Semantic score should be > 0");
        });

        Assert.NotNull(result.FamilyLabel);
    }

    /// <summary>
    /// Test semantic search finds products by technical specifications.
    /// </summary>
    [Fact]
    public async Task Search_TechnicalSpecQuery_FindsMatchingProducts()
    {
        using var scope = _fixture.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISearchOrchestrator>();

        var bomText = "36x48 inch window aluminum frame";

        var result = await orchestrator.SearchAsync(bomText);

        _output.WriteLine($"Query: {result.Query}");
        _output.WriteLine($"Matches: {result.MatchCount}");

        foreach (var match in result.Matches.Take(5))
        {
            _output.WriteLine($"  {match.Vendor} - {match.ModelName} (score: {match.SemanticScore:F4})");
        }

        Assert.True(result.MatchCount >= 1,
            $"Expected ≥1 semantic match for '{bomText}'. " +
            "Ensure product embeddings are generated (--generate-embeddings --all).");
        Assert.All(result.Matches, m =>
            Assert.True(m.SemanticScore > 0, "Semantic score should be > 0"));
    }
}
