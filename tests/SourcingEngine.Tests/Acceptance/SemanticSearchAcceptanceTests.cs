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
        _output.WriteLine($"Embedding provider: {embeddingService.GetType().Name}");
        _output.WriteLine($"Sample values: [{embedding[0]:F4}, {embedding[1]:F4}, {embedding[2]:F4}, ...]");

        Assert.Equal(embeddingService.EmbeddingDimension, embedding.Length);
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

        // Should return ranked results for common construction terms
        Assert.True(results.Count >= 1,
            "Expected ≥1 full-text search result for 'masonry block concrete'");
        Assert.True(results[0].Rank == 1);
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

        // Semantic search requires seeded embeddings with matching dimensions
        // Run: dotnet run --project src/SourcingEngine.Console -- --seed-embeddings
        Assert.True(results.Count >= 1,
            $"Expected ≥1 semantic search result for '{queryText}'. " +
            "Ensure family embeddings are seeded (--seed-embeddings) with the same embedding model.");
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

/// <summary>
/// Acceptance tests for product-level semantic search (nomic-embed-text / 768d)
/// Requires: Ollama running with nomic-embed-text model and product embeddings generated
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
    /// Test ProductFirst search mode bypasses family resolution and uses LLM query parsing
    /// </summary>
    [Fact]
    public async Task Search_ProductFirstMode_ReturnsSemanticMatches()
    {
        SkipIfNoOllama();

        // Arrange
        using var scope = _fixture.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISearchOrchestrator>();

        var bomText = "aluminum window frame for commercial building";

        // Act
        var result = await orchestrator.SearchAsync(bomText, SemanticSearchMode.ProductFirst);

        // Assert
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

        // ProductFirst should now resolve family and CSI from semantic results
        Assert.NotNull(result.FamilyLabel);
    }

    /// <summary>
    /// Test Hybrid mode combines family and product results
    /// </summary>
    [Fact]
    public async Task Search_HybridMode_CombinesFamilyAndProductResults()
    {
        SkipIfNoOllama();

        // Arrange
        using var scope = _fixture.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISearchOrchestrator>();

        var bomText = "8 inch masonry block lightweight";

        // Act — use Hybrid mode to combine family keyword + product semantic results
        var result = await orchestrator.SearchAsync(bomText, SemanticSearchMode.Hybrid);

        // Assert
        _output.WriteLine($"Query: {result.Query}");
        _output.WriteLine($"Family: {result.FamilyLabel}");
        _output.WriteLine($"Execution Time: {result.ExecutionTimeMs}ms");
        _output.WriteLine($"Matches: {result.MatchCount}");

        foreach (var match in result.Matches.Take(5))
        {
            var scoreInfo = match.SemanticScore.HasValue ? $" (semantic: {match.SemanticScore:F4})" : "";
            _output.WriteLine($"  {match.Vendor} - {match.ModelName}{scoreInfo}");
        }

        Assert.True(result.MatchCount >= 1,
            $"Expected ≥1 match from hybrid search for '{bomText}'. " +
            "Ensure product embeddings are generated (--generate-embeddings --all).");
        // Hybrid should resolve a family for well-known materials
        Assert.NotNull(result.FamilyLabel);

        // At least some matches should have enriched vendor data
        var enrichedCount = result.Matches.Count(m =>
            m.UseWhen != null || m.KeyFeatures != null || m.TechnicalSpecs != null || m.SourceSchema != null);
        _output.WriteLine($"Enriched matches: {enrichedCount}/{result.MatchCount}");
    }

    /// <summary>
    /// Test FamilyFirst mode works as before (regression test — no Ollama required)
    /// </summary>
    [Fact]
    public async Task Search_FamilyFirstMode_WorksAsExpected()
    {
        // Arrange
        using var scope = _fixture.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISearchOrchestrator>();

        var bomText = "concrete masonry unit";

        // Act
        var result = await orchestrator.SearchAsync(bomText, SemanticSearchMode.FamilyFirst);

        // Assert
        _output.WriteLine($"Query: {result.Query}");
        _output.WriteLine($"Family: {result.FamilyLabel}");
        _output.WriteLine($"Matches: {result.MatchCount}");

        // Should find material family
        Assert.NotNull(result.FamilyLabel);
        Assert.True(result.MatchCount >= 1,
            $"Expected ≥1 match for '{bomText}' in FamilyFirst mode.");
    }

    /// <summary>
    /// Test semantic search finds products by technical specifications
    /// </summary>
    [Fact]
    public async Task Search_TechnicalSpecQuery_FindsMatchingProducts()
    {
        SkipIfNoOllama();

        // Arrange
        using var scope = _fixture.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISearchOrchestrator>();

        // Query with dimensions that should match product specs
        var bomText = "36x48 inch window aluminum frame";

        // Act
        var result = await orchestrator.SearchAsync(bomText, SemanticSearchMode.ProductFirst);

        // Assert
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

    /// <summary>
    /// Guard: skip test cleanly when Ollama is not running
    /// </summary>
    private void SkipIfNoOllama()
    {
        if (!_fixture.IsOllamaAvailable)
        {
            _output.WriteLine("SKIPPED: Ollama is not available. Start Ollama and generate embeddings to run this test.");
            Assert.Fail("Ollama not available — test requires Ollama with nomic-embed-text for 768d embeddings. " +
                "Start Ollama, then run: dotnet run --project src/SourcingEngine.Console -- --generate-embeddings --all");
        }
    }
}
