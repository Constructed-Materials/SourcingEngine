using System.Text;
using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.MQEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SourcingEngine.BomExtraction.Lambda.Configuration;
using SourcingEngine.BomExtraction.Lambda.Models;
using SourcingEngine.BomExtraction.Lambda.Services;
using SourcingEngine.BomExtraction.Models;
using SourcingEngine.BomExtraction.Services;

namespace SourcingEngine.BomExtraction.Lambda.Tests;

public class FunctionTests : IDisposable
{
    private readonly Mock<IBomExtractionService> _mockExtraction;
    private readonly Mock<IRemoteFileFetcher> _mockFetcher;
    private readonly Mock<IRabbitMqResultPublisher> _mockPublisher;
    private readonly Mock<ILambdaContext> _mockContext;
    private readonly LambdaSettings _settings;
    private readonly Function _function;
    private readonly string _tempDir;

    public FunctionTests()
    {
        _mockExtraction = new Mock<IBomExtractionService>();
        _mockFetcher = new Mock<IRemoteFileFetcher>();
        _mockPublisher = new Mock<IRabbitMqResultPublisher>();
        _mockContext = new Mock<ILambdaContext>();

        _mockContext.Setup(c => c.AwsRequestId).Returns("test-request-id");
        _mockContext.Setup(c => c.RemainingTime).Returns(TimeSpan.FromMinutes(3));

        _tempDir = Path.Combine(Path.GetTempPath(), $"lambda_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _settings = new LambdaSettings
        {
            BrokerHost = "localhost",
            BrokerPort = 5672,
            ResultExchange = "bom.extraction",
            ResultRoutingKey = "extract.result",
            TempDirectory = _tempDir,
        };

        _function = new Function(
            _mockExtraction.Object,
            _mockFetcher.Object,
            _mockPublisher.Object,
            _settings,
            NullLogger<Function>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // =========================================================================
    // Helper methods
    // =========================================================================

    private static RabbitMQEvent CreateMqEvent(ExtractionRequestMessage request)
    {
        var json = JsonSerializer.Serialize(request);
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        return new RabbitMQEvent
        {
            EventSource = "aws:rmq",
            RmqMessagesByQueue = new Dictionary<string, List<RabbitMQEvent.RabbitMQMessage>>
            {
                ["bom-extraction-queue::/"] = new()
                {
                    new RabbitMQEvent.RabbitMQMessage
                    {
                        Data = base64,
                        BasicProperties = new RabbitMQEvent.BasicProperties
                        {
                            ContentType = "application/json",
                        },
                    }
                }
            }
        };
    }

    private static ExtractionRequestMessage CreateRequest(
        string traceId = "test-trace-001",
        string projectId = "42",
        string fileName = "estimate.csv",
        string url = "https://example.com/bom/estimate.csv")
    {
        return new ExtractionRequestMessage
        {
            TraceId = traceId,
            ProjectId = projectId,
            BomFiles = new List<BomFileReference>
            {
                new() { FileName = fileName, Url = url }
            },
        };
    }

    private ExtractionResult CreateSuccessResult(int itemCount = 3)
    {
        var items = Enumerable.Range(1, itemCount)
            .Select(i => new BomLineItem
            {
                BomItem = $"Item {i}",
                Spec = $"Spec for item {i}",
            })
            .ToList();

        return new ExtractionResult
        {
            SourceFile = "estimate.csv",
            Items = items,
            ModelUsed = "us.amazon.nova-pro-v1:0",
            InputTokens = 1000,
            OutputTokens = 500,
        };
    }

    // =========================================================================
    // Happy path tests
    // =========================================================================

    [Fact]
    public async Task FunctionHandler_SingleFile_ExtractsAndPublishesResult()
    {
        // Arrange
        var request = CreateRequest();
        var mqEvent = CreateMqEvent(request);
        var extractionResult = CreateSuccessResult(5);

        _mockFetcher
            .Setup(f => f.DownloadToPathAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("downloaded.csv");

        _mockExtraction
            .Setup(s => s.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(extractionResult);

        // Act
        await _function.FunctionHandler(mqEvent, _mockContext.Object);

        // Assert — download, extract, and publish were each called once
        _mockFetcher.Verify(
            f => f.DownloadToPathAsync("https://example.com/bom/estimate.csv", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _mockExtraction.Verify(
            s => s.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _mockPublisher.Verify(
            p => p.PublishResultAsync(
                It.Is<ExtractionResultMessage>(r =>
                    r.TraceId == "test-trace-001" &&
                    r.ProjectId == "42" &&
                    r.SourceFile == "estimate.csv" &&
                    r.ItemCount == 5),
                "test-trace-001",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FunctionHandler_MultipleFiles_ProcessesAllSequentially()
    {
        // Arrange
        var request = new ExtractionRequestMessage
        {
            TraceId = "trace-multi",
            ProjectId = "99",
            BomFiles = new List<BomFileReference>
            {
                new() { FileName = "file1.csv", Url = "https://example.com/file1.csv" },
                new() { FileName = "file2.pdf", Url = "https://example.com/file2.pdf" },
                new() { FileName = "file3.xlsx", Url = "https://example.com/file3.xlsx" },
            },
        };

        var mqEvent = CreateMqEvent(request);

        _mockFetcher
            .Setup(f => f.DownloadToPathAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("downloaded");

        _mockExtraction
            .Setup(s => s.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult(2));

        // Act
        await _function.FunctionHandler(mqEvent, _mockContext.Object);

        // Assert — each file gets its own download + extract + publish
        _mockFetcher.Verify(
            f => f.DownloadToPathAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));

        _mockExtraction.Verify(
            s => s.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));

        _mockPublisher.Verify(
            p => p.PublishResultAsync(It.IsAny<ExtractionResultMessage>(), "trace-multi", It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task FunctionHandler_InitializesPublisherOnce()
    {
        // Arrange
        var request = CreateRequest();
        var mqEvent = CreateMqEvent(request);

        _mockFetcher
            .Setup(f => f.DownloadToPathAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("downloaded.csv");

        _mockExtraction
            .Setup(s => s.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        // Act — invoke twice (simulating warm Lambda reuse)
        await _function.FunctionHandler(mqEvent, _mockContext.Object);
        await _function.FunctionHandler(mqEvent, _mockContext.Object);

        // Assert — publisher initialized only once
        _mockPublisher.Verify(p => p.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // Empty / null event tests
    // =========================================================================

    [Fact]
    public async Task FunctionHandler_EmptyEvent_ReturnsWithoutProcessing()
    {
        // Arrange
        var mqEvent = new RabbitMQEvent
        {
            EventSource = "aws:rmq",
            RmqMessagesByQueue = new Dictionary<string, List<RabbitMQEvent.RabbitMQMessage>>(),
        };

        // Act
        await _function.FunctionHandler(mqEvent, _mockContext.Object);

        // Assert — no processing should occur
        _mockFetcher.Verify(
            f => f.DownloadToPathAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FunctionHandler_NullMessagesByQueue_ReturnsWithoutProcessing()
    {
        // Arrange
        var mqEvent = new RabbitMQEvent
        {
            EventSource = "aws:rmq",
            RmqMessagesByQueue = null,
        };

        // Act
        await _function.FunctionHandler(mqEvent, _mockContext.Object);

        // Assert
        _mockFetcher.Verify(
            f => f.DownloadToPathAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // =========================================================================
    // Error handling tests
    // =========================================================================

    [Fact]
    public async Task FunctionHandler_EmptyMessageData_ThrowsInvalidOperationException()
    {
        // Arrange
        var mqEvent = new RabbitMQEvent
        {
            EventSource = "aws:rmq",
            RmqMessagesByQueue = new Dictionary<string, List<RabbitMQEvent.RabbitMQMessage>>
            {
                ["bom-extraction-queue::/"] = new()
                {
                    new RabbitMQEvent.RabbitMQMessage { Data = "" }
                }
            }
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _function.FunctionHandler(mqEvent, _mockContext.Object));
    }

    [Fact]
    public async Task FunctionHandler_InvalidJson_ThrowsJsonException()
    {
        // Arrange
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("not valid json!!!"));
        var mqEvent = new RabbitMQEvent
        {
            EventSource = "aws:rmq",
            RmqMessagesByQueue = new Dictionary<string, List<RabbitMQEvent.RabbitMQMessage>>
            {
                ["bom-extraction-queue::/"] = new()
                {
                    new RabbitMQEvent.RabbitMQMessage { Data = base64 }
                }
            }
        };

        // Act & Assert
        await Assert.ThrowsAsync<JsonException>(
            () => _function.FunctionHandler(mqEvent, _mockContext.Object));
    }

    [Fact]
    public async Task FunctionHandler_MissingTraceId_ThrowsInvalidOperationException()
    {
        // Arrange — valid JSON but empty traceId
        var request = new ExtractionRequestMessage
        {
            TraceId = "",
            ProjectId = "1",
            BomFiles = new List<BomFileReference> { new() { FileName = "f.csv", Url = "https://u" } },
        };
        var mqEvent = CreateMqEvent(request);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _function.FunctionHandler(mqEvent, _mockContext.Object));
    }

    [Fact]
    public async Task FunctionHandler_DownloadFailure_PropagatesException()
    {
        // Arrange
        var request = CreateRequest();
        var mqEvent = CreateMqEvent(request);

        _mockFetcher
            .Setup(f => f.DownloadToPathAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act & Assert — exception propagates (Lambda retry handles it)
        await Assert.ThrowsAsync<HttpRequestException>(
            () => _function.FunctionHandler(mqEvent, _mockContext.Object));
    }

    [Fact]
    public async Task FunctionHandler_ExtractionFailure_PropagatesException()
    {
        // Arrange
        var request = CreateRequest();
        var mqEvent = CreateMqEvent(request);

        _mockFetcher
            .Setup(f => f.DownloadToPathAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("downloaded.csv");

        _mockExtraction
            .Setup(s => s.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Bedrock timeout"));

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(
            () => _function.FunctionHandler(mqEvent, _mockContext.Object));
    }

    // =========================================================================
    // Temp directory cleanup test
    // =========================================================================

    [Fact]
    public async Task FunctionHandler_CleansTempDirectoryAfterProcessing()
    {
        // Arrange
        var request = CreateRequest(traceId: "cleanup-trace");
        var mqEvent = CreateMqEvent(request);

        _mockFetcher
            .Setup(f => f.DownloadToPathAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("downloaded.csv");

        _mockExtraction
            .Setup(s => s.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        // Act
        await _function.FunctionHandler(mqEvent, _mockContext.Object);

        // Assert — the per-trace temp directory should be cleaned up
        var traceDir = Path.Combine(_tempDir, "cleanup-trace");
        Assert.False(Directory.Exists(traceDir), "Temp directory should be cleaned up after processing");
    }
}
