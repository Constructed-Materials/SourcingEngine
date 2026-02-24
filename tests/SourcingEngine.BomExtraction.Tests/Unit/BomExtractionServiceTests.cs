using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SourcingEngine.BomExtraction.Configuration;
using SourcingEngine.BomExtraction.Models;
using SourcingEngine.BomExtraction.Parsing;
using SourcingEngine.BomExtraction.Services;

namespace SourcingEngine.BomExtraction.Tests.Unit;

public class BomExtractionServiceTests : IDisposable
{
    private readonly Mock<IAmazonBedrockRuntime> _mockClient;
    private readonly BomExtractionService _service;
    private readonly BomExtractionSettings _settings;
    private readonly string _tempDir;

    public BomExtractionServiceTests()
    {
        _mockClient = new Mock<IAmazonBedrockRuntime>();
        _settings = new BomExtractionSettings
        {
            Region = "us-east-2",
            ModelId = "us.amazon.nova-pro-v1:0",
            MaxTokens = 5000,
            Temperature = 0.0f,
            TimeoutSeconds = 120,
        };

        var parser = new JsonResponseParser(NullLogger<JsonResponseParser>.Instance);

        _service = new BomExtractionService(
            _mockClient.Object,
            NullLogger<BomExtractionService>.Instance,
            Options.Create(_settings),
            parser);

        _tempDir = Path.Combine(Path.GetTempPath(), $"bom_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _service.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ---------------------------------------------------------------
    // Helper: create a temp file with content
    // ---------------------------------------------------------------

    private string CreateTempFile(string name, string content = "test content")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private void SetupMockResponse(string responseText, string stopReason = "end_turn",
        int inputTokens = 100, int outputTokens = 200)
    {
        var response = new ConverseResponse
        {
            Output = new ConverseOutput
            {
                Message = new Message
                {
                    Content = new List<ContentBlock>
                    {
                        new() { Text = responseText }
                    }
                }
            },
            StopReason = stopReason,
            Usage = new TokenUsage
            {
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
            }
        };

        _mockClient
            .Setup(c => c.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
    }

    // ---------------------------------------------------------------
    // Successful extraction
    // ---------------------------------------------------------------

    [Fact]
    public async Task ExtractAsync_ValidCsvFile_ReturnsExtractionResult()
    {
        var filePath = CreateTempFile("estimate.csv", "Material,Qty\nBlock,100");
        SetupMockResponse("""[{"bom_item": "Block", "spec": "8 inch Block", "quantity": 100}]""");

        var result = await _service.ExtractAsync(filePath);

        Assert.Equal(filePath, result.SourceFile);
        Assert.Equal("us.amazon.nova-pro-v1:0", result.ModelUsed);
        Assert.Single(result.Items);
        Assert.Equal("Block", result.Items[0].BomItem);
        Assert.Equal(100, result.Items[0].Quantity);
        Assert.Equal(100, result.InputTokens);
        Assert.Equal(200, result.OutputTokens);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task ExtractAsync_MultipleItems_AllReturned()
    {
        var filePath = CreateTempFile("multi.csv", "dummy");
        SetupMockResponse("""
            [
                {"bom_item": "CMU Block", "spec": "8 inch CMU Block", "quantity": 1200},
                {"bom_item": "Rebar", "spec": "#5 Rebar 20ft", "quantity": 350},
                {"bom_item": "Mortar", "spec": "Type S Mortar", "quantity": 150}
            ]
            """);

        var result = await _service.ExtractAsync(filePath);

        Assert.Equal(3, result.ItemCount);
    }

    // ---------------------------------------------------------------
    // File format handling
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("test.pdf")]
    [InlineData("test.csv")]
    [InlineData("test.xlsx")]
    [InlineData("test.xls")]
    [InlineData("test.doc")]
    [InlineData("test.docx")]
    [InlineData("test.html")]
    [InlineData("test.txt")]
    [InlineData("test.md")]
    public async Task ExtractAsync_AllSupportedFormats_DoesNotThrow(string fileName)
    {
        var filePath = CreateTempFile(fileName);
        SetupMockResponse("""[{"bom_item": "Test", "spec": "Test spec"}]""");

        var result = await _service.ExtractAsync(filePath);

        Assert.NotNull(result);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task ExtractAsync_UnsupportedFormat_ThrowsArgumentException()
    {
        var filePath = CreateTempFile("test.zip");

        await Assert.ThrowsAsync<ArgumentException>(() => _service.ExtractAsync(filePath));
    }

    // ---------------------------------------------------------------
    // Input validation
    // ---------------------------------------------------------------

    [Fact]
    public async Task ExtractAsync_NullFilePath_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ExtractAsync(null!));
    }

    [Fact]
    public async Task ExtractAsync_EmptyFilePath_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ExtractAsync(""));
    }

    [Fact]
    public async Task ExtractAsync_FileNotFound_ThrowsFileNotFoundException()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _service.ExtractAsync("/nonexistent/file.csv"));
    }

    // ---------------------------------------------------------------
    // Truncation warning
    // ---------------------------------------------------------------

    [Fact]
    public async Task ExtractAsync_TruncatedResponse_AddsWarning()
    {
        var filePath = CreateTempFile("big.csv", "lots of data");
        SetupMockResponse(
            """[{"bom_item": "Block", "spec": "CMU Block", "quantity": 100}]""",
            stopReason: "max_tokens",
            outputTokens: 5000);

        var result = await _service.ExtractAsync(filePath);

        Assert.Single(result.Items);
        Assert.Single(result.Warnings);
        Assert.Contains("truncated", result.Warnings[0], StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------
    // Empty response
    // ---------------------------------------------------------------

    [Fact]
    public async Task ExtractAsync_EmptyResponse_ReturnsEmptyResultWithWarning()
    {
        var filePath = CreateTempFile("empty.csv", "data");

        var response = new ConverseResponse
        {
            Output = new ConverseOutput
            {
                Message = new Message
                {
                    Content = new List<ContentBlock>
                    {
                        new() { Text = "" }
                    }
                }
            },
            StopReason = "end_turn",
            Usage = new TokenUsage { InputTokens = 50, OutputTokens = 0 }
        };

        _mockClient
            .Setup(c => c.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _service.ExtractAsync(filePath);

        Assert.Empty(result.Items);
        Assert.Contains(result.Warnings, w => w.Contains("empty", StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------
    // Bedrock API error handling
    // ---------------------------------------------------------------

    [Fact]
    public async Task ExtractAsync_BedrockApiError_ReturnsWarningNotThrow()
    {
        var filePath = CreateTempFile("error.csv", "data");

        _mockClient
            .Setup(c => c.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonBedrockRuntimeException("Throttled"));

        var result = await _service.ExtractAsync(filePath);

        Assert.Empty(result.Items);
        Assert.Single(result.Warnings);
        Assert.Contains("Bedrock API error", result.Warnings[0]);
    }

    // ---------------------------------------------------------------
    // Converse request structure
    // ---------------------------------------------------------------

    [Fact]
    public async Task ExtractAsync_SendsCorrectConverseRequest()
    {
        var filePath = CreateTempFile("verify.csv", "some,data");
        SetupMockResponse("[]");

        ConverseRequest? capturedRequest = null;
        _mockClient
            .Setup(c => c.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ConverseRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new ConverseResponse
            {
                Output = new ConverseOutput
                {
                    Message = new Message
                    {
                        Content = new List<ContentBlock> { new() { Text = "[]" } }
                    }
                },
                StopReason = "end_turn",
                Usage = new TokenUsage { InputTokens = 10, OutputTokens = 5 }
            });

        await _service.ExtractAsync(filePath);

        Assert.NotNull(capturedRequest);
        Assert.Equal("us.amazon.nova-pro-v1:0", capturedRequest!.ModelId);
        Assert.Single(capturedRequest.System);
        Assert.Contains("BOM", capturedRequest.System[0].Text);
        Assert.Single(capturedRequest.Messages);
        Assert.Equal(ConversationRole.User, capturedRequest.Messages[0].Role);

        // Should have 2 content blocks: DocumentBlock + text prompt
        Assert.Equal(2, capturedRequest.Messages[0].Content.Count);
        Assert.NotNull(capturedRequest.Messages[0].Content[0].Document);
        Assert.NotNull(capturedRequest.Messages[0].Content[1].Text);

        // Verify inference config
        Assert.Equal(5000, capturedRequest.InferenceConfig.MaxTokens);
        Assert.Equal(0.0f, capturedRequest.InferenceConfig.Temperature);
    }

    [Fact]
    public void BuildConverseRequest_SetsDocumentFormat()
    {
        var fileBytes = "test"u8.ToArray();

        var request = _service.BuildConverseRequest(fileBytes, SupportedFileFormat.Csv, "test.csv");

        var docBlock = request.Messages[0].Content[0].Document;
        Assert.Equal("csv", docBlock.Format.Value);
        Assert.Equal("test", docBlock.Name);
    }

    [Fact]
    public void BuildConverseRequest_SetsDocumentBytes()
    {
        var fileBytes = new byte[] { 1, 2, 3, 4, 5 };

        var request = _service.BuildConverseRequest(fileBytes, SupportedFileFormat.Pdf, "doc.pdf");

        var stream = request.Messages[0].Content[0].Document.Source.Bytes;
        Assert.NotNull(stream);
        Assert.Equal(5, stream.Length);
    }
}
