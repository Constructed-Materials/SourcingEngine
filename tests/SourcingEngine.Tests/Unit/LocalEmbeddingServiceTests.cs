using SourcingEngine.Core.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace SourcingEngine.Tests.Unit;

/// <summary>
/// Unit tests for LocalEmbeddingService
/// </summary>
public class LocalEmbeddingServiceTests
{
    private readonly LocalEmbeddingService _service;
    private readonly IMemoryCache _cache;
    private readonly Mock<ILogger<LocalEmbeddingService>> _loggerMock;

    public LocalEmbeddingServiceTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
        _loggerMock = new Mock<ILogger<LocalEmbeddingService>>();
        _service = new LocalEmbeddingService(_cache, _loggerMock.Object);
    }

    [Fact]
    public void EmbeddingDimension_Returns384()
    {
        Assert.Equal(384, _service.EmbeddingDimension);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithValidText_ReturnsEmbedding()
    {
        // Arrange
        var text = "concrete masonry unit";

        // Act
        var embedding = await _service.GenerateEmbeddingAsync(text);

        // Assert
        Assert.NotNull(embedding);
        Assert.Equal(384, embedding.Length);
        Assert.All(embedding, value => Assert.InRange(value, -10f, 10f));
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithSameText_ReturnsCachedResult()
    {
        // Arrange
        var text = "masonry block";

        // Act
        var embedding1 = await _service.GenerateEmbeddingAsync(text);
        var embedding2 = await _service.GenerateEmbeddingAsync(text);

        // Assert - should be exactly the same array reference due to caching
        Assert.Equal(embedding1, embedding2);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithDifferentTexts_ReturnsDifferentEmbeddings()
    {
        // Arrange
        var text1 = "concrete block";
        var text2 = "aluminum window";

        // Act
        var embedding1 = await _service.GenerateEmbeddingAsync(text1);
        var embedding2 = await _service.GenerateEmbeddingAsync(text2);

        // Assert - embeddings should be different
        Assert.NotEqual(embedding1, embedding2);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithEmptyText_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _service.GenerateEmbeddingAsync(""));
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithWhitespaceText_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _service.GenerateEmbeddingAsync("   "));
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_WithMultipleTexts_ReturnsAllEmbeddings()
    {
        // Arrange
        var texts = new[] { "concrete", "steel", "wood" };

        // Act
        var embeddings = await _service.GenerateEmbeddingsAsync(texts);

        // Assert
        Assert.Equal(3, embeddings.Count);
        Assert.All(embeddings, e => Assert.Equal(384, e.Length));
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_SimilarTexts_ProduceSimilarEmbeddings()
    {
        // Arrange
        var text1 = "concrete masonry unit";
        var text2 = "cmu block";

        // Act
        var embedding1 = await _service.GenerateEmbeddingAsync(text1);
        var embedding2 = await _service.GenerateEmbeddingAsync(text2);

        // Calculate cosine similarity
        var similarity = CosineSimilarity(embedding1, embedding2);

        // Assert - related terms should have some similarity (> 0.3)
        Assert.True(similarity > 0.3, $"Expected similarity > 0.3, got {similarity}");
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
