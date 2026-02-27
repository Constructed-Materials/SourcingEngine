using System.Text;
using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.MQEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SourcingEngine.Common.Models;
using SourcingEngine.Core.Models;
using SourcingEngine.Core.Services;
using SourcingEngine.Search.Lambda.Configuration;
using SourcingEngine.Search.Lambda.Services;
using Xunit;

namespace SourcingEngine.Search.Lambda.Tests;

public class FunctionTests
{
    private readonly Mock<ISearchOrchestrator> _mockOrchestrator;
    private readonly Mock<IRabbitMqSearchResultPublisher> _mockPublisher;
    private readonly Mock<ILambdaContext> _mockContext;
    private readonly SearchLambdaSettings _settings;
    private readonly Function _function;

    public FunctionTests()
    {
        _mockOrchestrator = new Mock<ISearchOrchestrator>();
        _mockPublisher = new Mock<IRabbitMqSearchResultPublisher>();
        _mockContext = new Mock<ILambdaContext>();

        _mockContext.Setup(c => c.AwsRequestId).Returns("test-request-id");
        _mockContext.Setup(c => c.RemainingTime).Returns(TimeSpan.FromMinutes(5));

        _settings = new SearchLambdaSettings
        {
            BrokerHost = "localhost",
            BrokerPort = 5672,
            ResultExchange = "sourcing.engine",
            ResultRoutingKey = "search.result",
            ZeroResultRoutingKey = "search.zero-result",
        };

        _function = new Function(
            _mockOrchestrator.Object,
            _mockPublisher.Object,
            _settings,
            NullLogger<Function>.Instance);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static RabbitMQEvent CreateMqEvent(ExtractionResultMessage extractionResult)
    {
        var json = JsonSerializer.Serialize(extractionResult);
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        return new RabbitMQEvent
        {
            EventSource = "aws:rmq",
            RmqMessagesByQueue = new Dictionary<string, List<RabbitMQEvent.RabbitMQMessage>>
            {
                ["bom-extraction-result-queue::/"] = new()
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

    private static ExtractionResultMessage CreateExtractionResult(
        string traceId = "trace-123",
        string projectId = "proj-456",
        params BomLineItem[] items)
    {
        return new ExtractionResultMessage
        {
            TraceId = traceId,
            ProjectId = projectId,
            SourceFile = "test-estimate.csv",
            SourceUrl = "https://example.com/test-estimate.csv",
            ItemCount = items.Length,
            Items = items.ToList(),
            Warnings = new List<string>(),
            ModelUsed = "test-model",
        };
    }

    private static SourcingResult CreateSourcingResult(
        string traceId,
        string projectId,
        List<BomItemSearchResult> items)
    {
        return new SourcingResult
        {
            TraceId = traceId,
            ProjectId = projectId,
            SourceFile = "test-estimate.csv",
            Items = items,
            TotalExecutionTimeMs = 1000,
            Warnings = new List<string>(),
        };
    }

    private static BomItemSearchResult CreateItemResult(
        string bomItem, string spec, int matchCount)
    {
        var matches = Enumerable.Range(0, matchCount).Select(i => new ProductMatch
        {
            ProductId = Guid.NewGuid(),
            Vendor = $"vendor-{i}",
            ModelName = $"model-{i}",
        }).ToList();

        return new BomItemSearchResult
        {
            BomItemName = bomItem,
            Spec = spec,
            Quantity = 10,
            SearchResult = new SearchResult
            {
                Query = spec,
                FamilyLabel = "test-family",
                CsiCode = "042200",
                Matches = matches,
                ExecutionTimeMs = 100,
            },
        };
    }

    // =========================================================================
    // Tests
    // =========================================================================

    [Fact]
    public async Task FunctionHandler_HappyPath_MixedResults_PublishesBothQueues()
    {
        // Arrange: 2 BOM items — one with matches, one without
        var items = new[]
        {
            new BomLineItem { BomItem = "CMU Block", Spec = "8 inch masonry block" },
            new BomLineItem { BomItem = "Unknown Item", Spec = "some unknown product" },
        };
        var extraction = CreateExtractionResult(items: items);

        var itemResults = new List<BomItemSearchResult>
        {
            CreateItemResult("CMU Block", "8 inch masonry block", 5),
            CreateItemResult("Unknown Item", "some unknown product", 0),
        };
        var sourcingResult = CreateSourcingResult("trace-123", "proj-456", itemResults);

        _mockOrchestrator
            .Setup(o => o.SearchAsync(It.IsAny<SourcingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourcingResult);

        var mqEvent = CreateMqEvent(extraction);

        // Act
        await _function.FunctionHandler(mqEvent, _mockContext.Object);

        // Assert: published to results queue (1 item with 5 matches)
        _mockPublisher.Verify(p => p.PublishResultAsync(
            It.Is<SourcingResultMessage>(m =>
                m.TraceId == "trace-123" &&
                m.Items.Count == 1 &&
                m.Items[0].BomItem == "CMU Block" &&
                m.Items[0].MatchCount == 5),
            "trace-123",
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert: published to zero-results queue (1 item with 0 matches)
        _mockPublisher.Verify(p => p.PublishZeroResultsAsync(
            It.Is<SourcingZeroResultsMessage>(m =>
                m.TraceId == "trace-123" &&
                m.Items.Count == 1 &&
                m.Items[0].BomItem == "Unknown Item"),
            "trace-123",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FunctionHandler_AllItemsHaveMatches_OnlyResultsPublished()
    {
        var items = new[]
        {
            new BomLineItem { BomItem = "CMU Block", Spec = "8 inch masonry block" },
            new BomLineItem { BomItem = "Rebar", Spec = "#4 rebar" },
        };
        var extraction = CreateExtractionResult(items: items);

        var itemResults = new List<BomItemSearchResult>
        {
            CreateItemResult("CMU Block", "8 inch masonry block", 5),
            CreateItemResult("Rebar", "#4 rebar", 3),
        };
        var sourcingResult = CreateSourcingResult("trace-123", "proj-456", itemResults);

        _mockOrchestrator
            .Setup(o => o.SearchAsync(It.IsAny<SourcingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourcingResult);

        await _function.FunctionHandler(CreateMqEvent(extraction), _mockContext.Object);

        // Results published
        _mockPublisher.Verify(p => p.PublishResultAsync(
            It.Is<SourcingResultMessage>(m => m.Items.Count == 2),
            "trace-123",
            It.IsAny<CancellationToken>()), Times.Once);

        // Zero-results NOT published
        _mockPublisher.Verify(p => p.PublishZeroResultsAsync(
            It.IsAny<SourcingZeroResultsMessage>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FunctionHandler_AllItemsZeroMatches_OnlyZeroResultsPublished()
    {
        var items = new[]
        {
            new BomLineItem { BomItem = "Unknown A", Spec = "unknown product A" },
            new BomLineItem { BomItem = "Unknown B", Spec = "unknown product B" },
        };
        var extraction = CreateExtractionResult(items: items);

        var itemResults = new List<BomItemSearchResult>
        {
            CreateItemResult("Unknown A", "unknown product A", 0),
            CreateItemResult("Unknown B", "unknown product B", 0),
        };
        var sourcingResult = CreateSourcingResult("trace-123", "proj-456", itemResults);

        _mockOrchestrator
            .Setup(o => o.SearchAsync(It.IsAny<SourcingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourcingResult);

        await _function.FunctionHandler(CreateMqEvent(extraction), _mockContext.Object);

        // Results NOT published
        _mockPublisher.Verify(p => p.PublishResultAsync(
            It.IsAny<SourcingResultMessage>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);

        // Zero-results published
        _mockPublisher.Verify(p => p.PublishZeroResultsAsync(
            It.Is<SourcingZeroResultsMessage>(m => m.Items.Count == 2),
            "trace-123",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FunctionHandler_EmptyEvent_ReturnsWithoutPublishing()
    {
        var mqEvent = new RabbitMQEvent
        {
            EventSource = "aws:rmq",
            RmqMessagesByQueue = new Dictionary<string, List<RabbitMQEvent.RabbitMQMessage>>(),
        };

        await _function.FunctionHandler(mqEvent, _mockContext.Object);

        _mockOrchestrator.Verify(o => o.SearchAsync(
            It.IsAny<SourcingRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockPublisher.Verify(p => p.PublishResultAsync(
            It.IsAny<SourcingResultMessage>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FunctionHandler_NullQueues_ReturnsWithoutPublishing()
    {
        var mqEvent = new RabbitMQEvent
        {
            EventSource = "aws:rmq",
            RmqMessagesByQueue = null,
        };

        await _function.FunctionHandler(mqEvent, _mockContext.Object);

        _mockOrchestrator.Verify(o => o.SearchAsync(
            It.IsAny<SourcingRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FunctionHandler_BadJson_ThrowsJsonException()
    {
        var badBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("NOT VALID JSON {{{"));
        var mqEvent = new RabbitMQEvent
        {
            EventSource = "aws:rmq",
            RmqMessagesByQueue = new Dictionary<string, List<RabbitMQEvent.RabbitMQMessage>>
            {
                ["bom-extraction-result-queue::/"] = new()
                {
                    new RabbitMQEvent.RabbitMQMessage
                    {
                        Data = badBase64,
                        BasicProperties = new RabbitMQEvent.BasicProperties
                        {
                            ContentType = "application/json",
                        },
                    }
                }
            }
        };

        await Assert.ThrowsAsync<JsonException>(
            () => _function.FunctionHandler(mqEvent, _mockContext.Object));
    }

    [Fact]
    public async Task FunctionHandler_MissingTraceId_ThrowsInvalidOperation()
    {
        var extraction = new ExtractionResultMessage
        {
            TraceId = "", // Missing
            ProjectId = "proj-456",
            SourceFile = "test.csv",
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _function.FunctionHandler(CreateMqEvent(extraction), _mockContext.Object));
    }

    [Fact]
    public async Task FunctionHandler_EmptyMessageData_ThrowsInvalidOperation()
    {
        var mqEvent = new RabbitMQEvent
        {
            EventSource = "aws:rmq",
            RmqMessagesByQueue = new Dictionary<string, List<RabbitMQEvent.RabbitMQMessage>>
            {
                ["bom-extraction-result-queue::/"] = new()
                {
                    new RabbitMQEvent.RabbitMQMessage
                    {
                        Data = "",
                        BasicProperties = new RabbitMQEvent.BasicProperties(),
                    }
                }
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _function.FunctionHandler(mqEvent, _mockContext.Object));
    }

    [Fact]
    public async Task FunctionHandler_PublisherInitCalledOnce()
    {
        var items = new[] { new BomLineItem { BomItem = "CMU", Spec = "block" } };
        var extraction = CreateExtractionResult(items: items);

        var itemResults = new List<BomItemSearchResult>
        {
            CreateItemResult("CMU", "block", 3),
        };
        var sourcingResult = CreateSourcingResult("trace-123", "proj-456", itemResults);

        _mockOrchestrator
            .Setup(o => o.SearchAsync(It.IsAny<SourcingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourcingResult);

        // Invoke twice (simulating warm-start reuse)
        await _function.FunctionHandler(CreateMqEvent(extraction), _mockContext.Object);
        await _function.FunctionHandler(CreateMqEvent(extraction), _mockContext.Object);

        // InitializeAsync should only be called once
        _mockPublisher.Verify(p => p.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FunctionHandler_ResultMessage_ContainsCorrectProductDetails()
    {
        var items = new[] { new BomLineItem { BomItem = "CMU Block", Spec = "8 inch masonry block", Quantity = 100 } };
        var extraction = CreateExtractionResult(items: items);

        var itemResults = new List<BomItemSearchResult>
        {
            CreateItemResult("CMU Block", "8 inch masonry block", 2),
        };
        var sourcingResult = CreateSourcingResult("trace-123", "proj-456", itemResults);

        _mockOrchestrator
            .Setup(o => o.SearchAsync(It.IsAny<SourcingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourcingResult);

        SourcingResultMessage? publishedMessage = null;
        _mockPublisher
            .Setup(p => p.PublishResultAsync(It.IsAny<SourcingResultMessage>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<SourcingResultMessage, string, CancellationToken>((msg, _, _) => publishedMessage = msg);

        await _function.FunctionHandler(CreateMqEvent(extraction), _mockContext.Object);

        Assert.NotNull(publishedMessage);
        Assert.Equal("trace-123", publishedMessage!.TraceId);
        Assert.Equal("proj-456", publishedMessage.ProjectId);
        Assert.Equal("test-estimate.csv", publishedMessage.SourceFile);
        Assert.Single(publishedMessage.Items);
        Assert.Equal(2, publishedMessage.Items[0].MatchCount);
        Assert.Equal(2, publishedMessage.Items[0].Matches.Count);
        Assert.Equal("test-family", publishedMessage.Items[0].FamilyLabel);
        Assert.Equal("042200", publishedMessage.Items[0].CsiCode);
    }

    [Fact]
    public async Task FunctionHandler_OrchestratorThrows_ExceptionPropagates()
    {
        var items = new[] { new BomLineItem { BomItem = "CMU", Spec = "block" } };
        var extraction = CreateExtractionResult(items: items);

        _mockOrchestrator
            .Setup(o => o.SearchAsync(It.IsAny<SourcingRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB connection failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _function.FunctionHandler(CreateMqEvent(extraction), _mockContext.Object));
    }
}
