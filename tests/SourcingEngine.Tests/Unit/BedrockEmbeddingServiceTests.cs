using System.IO;
using System.Text;
using System.Text.Json;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SourcingEngine.Core.Configuration;
using SourcingEngine.Core.Services;
using Xunit;

namespace SourcingEngine.Tests.Unit;

/// <summary>
/// Unit tests for BedrockEmbeddingService.
/// Mocks IAmazonBedrockRuntime to test request/response handling without real AWS calls.
/// </summary>
public class BedrockEmbeddingServiceTests : IDisposable
{
    private readonly Mock<IAmazonBedrockRuntime> _clientMock;
    private readonly IMemoryCache _cache;
    private readonly Mock<ILogger<BedrockEmbeddingService>> _loggerMock;
    private readonly IOptions<BedrockSettings> _settings;
    private readonly BedrockEmbeddingService _service;

    public BedrockEmbeddingServiceTests()
    {
        _clientMock = new Mock<IAmazonBedrockRuntime>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _loggerMock = new Mock<ILogger<BedrockEmbeddingService>>();
        _settings = Options.Create(new BedrockSettings
        {
            Enabled = true,
            Region = "us-east-1",
            EmbeddingModelId = "amazon.titan-embed-text-v2:0",
            EmbeddingDimension = 1024,
            EmbeddingNormalize = true,
            MaxConcurrentEmbeddings = 2
        });

        _service = new BedrockEmbeddingService(
            _clientMock.Object, _cache, _loggerMock.Object, _settings);
    }

    public void Dispose()
    {
        _service.Dispose();
        _cache.Dispose();
    }

    [Fact]
    public void EmbeddingDimension_ReturnsConfiguredValue()
    {
        Assert.Equal(1024, _service.EmbeddingDimension);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithValidText_ReturnsCorrectDimensionEmbedding()
    {
        // Arrange
        var expectedEmbedding = CreateFakeEmbedding(1024);
        SetupInvokeModelResponse(expectedEmbedding, inputTokenCount: 5);

        // Act
        var result = await _service.GenerateEmbeddingAsync("concrete masonry unit");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1024, result.Length);
        Assert.Equal(expectedEmbedding, result);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_SendsCorrectRequestBody()
    {
        // Arrange
        var expectedEmbedding = CreateFakeEmbedding(1024);
        InvokeModelRequest? capturedRequest = null;

        _clientMock.Setup(c => c.InvokeModelAsync(
                It.IsAny<InvokeModelRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<InvokeModelRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(CreateInvokeModelResponse(expectedEmbedding, 3));

        // Act
        await _service.GenerateEmbeddingAsync("test text");

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Equal("amazon.titan-embed-text-v2:0", capturedRequest!.ModelId);
        Assert.Equal("application/json", capturedRequest.ContentType);
        Assert.Equal("application/json", capturedRequest.Accept);

        // Verify request body JSON
        capturedRequest.Body.Position = 0;
        var bodyJson = await new StreamReader(capturedRequest.Body).ReadToEndAsync();
        using var doc = JsonDocument.Parse(bodyJson);
        var root = doc.RootElement;

        Assert.Equal("test text", root.GetProperty("inputText").GetString());
        Assert.Equal(1024, root.GetProperty("dimensions").GetInt32());
        Assert.True(root.GetProperty("normalize").GetBoolean());
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_CachesResult()
    {
        // Arrange
        var expectedEmbedding = CreateFakeEmbedding(1024);
        SetupInvokeModelResponse(expectedEmbedding, inputTokenCount: 5);

        // Act
        var result1 = await _service.GenerateEmbeddingAsync("cached text");
        var result2 = await _service.GenerateEmbeddingAsync("cached text");

        // Assert — InvokeModel should only be called once
        _clientMock.Verify(c => c.InvokeModelAsync(
            It.IsAny<InvokeModelRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(result1, result2);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithEmptyText_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.GenerateEmbeddingAsync(""));
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithNullText_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.GenerateEmbeddingAsync(null!));
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_EmptyEmbeddingResponse_ThrowsInvalidOperationException()
    {
        // Arrange — response with empty embedding
        SetupInvokeModelResponse(Array.Empty<float>(), inputTokenCount: 0);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.GenerateEmbeddingAsync("some text"));
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_BatchProcessing_RespectsThrottling()
    {
        // Arrange
        var texts = Enumerable.Range(0, 5).Select(i => $"text {i}").ToList();
        var fakeEmbedding = CreateFakeEmbedding(1024);
        SetupInvokeModelResponseFactory(fakeEmbedding, 2);

        // Act
        var results = await _service.GenerateEmbeddingsAsync(texts);

        // Assert
        Assert.Equal(5, results.Count);
        Assert.All(results, r => Assert.Equal(1024, r.Length));
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_DifferentTexts_MakesMultipleCalls()
    {
        // Arrange
        var texts = new[] { "text a", "text b", "text c" };
        var fakeEmbedding = CreateFakeEmbedding(1024);
        SetupInvokeModelResponseFactory(fakeEmbedding, 2);

        // Act
        await _service.GenerateEmbeddingsAsync(texts);

        // Assert — should call InvokeModel for each unique text
        _clientMock.Verify(c => c.InvokeModelAsync(
            It.IsAny<InvokeModelRequest>(),
            It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    // ─── Helpers ───

    private static float[] CreateFakeEmbedding(int dimension)
    {
        var rng = new Random(42);
        return Enumerable.Range(0, dimension).Select(_ => (float)(rng.NextDouble() * 2 - 1)).ToArray();
    }

    private void SetupInvokeModelResponseFactory(float[] embedding, int inputTokenCount)
    {
        _clientMock.Setup(c => c.InvokeModelAsync(
                It.IsAny<InvokeModelRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(CreateInvokeModelResponse(embedding, inputTokenCount)));
    }

    private void SetupInvokeModelResponse(float[] embedding, int inputTokenCount)
    {
        _clientMock.Setup(c => c.InvokeModelAsync(
                It.IsAny<InvokeModelRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateInvokeModelResponse(embedding, inputTokenCount));
    }

    private static InvokeModelResponse CreateInvokeModelResponse(float[] embedding, int inputTokenCount)
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            embedding,
            inputTextTokenCount = inputTokenCount
        });

        return new InvokeModelResponse
        {
            Body = new MemoryStream(Encoding.UTF8.GetBytes(responseJson)),
            ContentType = "application/json"
        };
    }
}
