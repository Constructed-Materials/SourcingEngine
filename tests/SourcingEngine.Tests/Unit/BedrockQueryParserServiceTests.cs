using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SourcingEngine.Core.Configuration;
using SourcingEngine.Core.Services;
using Xunit;

namespace SourcingEngine.Tests.Unit;

/// <summary>
/// Unit tests for BedrockQueryParserService.
/// Mocks IAmazonBedrockRuntime to test Converse API interaction without real AWS calls.
/// </summary>
public class BedrockQueryParserServiceTests : IDisposable
{
    private readonly Mock<IAmazonBedrockRuntime> _clientMock;
    private readonly Mock<ILogger<BedrockQueryParserService>> _loggerMock;
    private readonly IOptions<BedrockSettings> _settings;
    private readonly BedrockQueryParserService _service;

    public BedrockQueryParserServiceTests()
    {
        _clientMock = new Mock<IAmazonBedrockRuntime>();
        _loggerMock = new Mock<ILogger<BedrockQueryParserService>>();
        _settings = Options.Create(new BedrockSettings
        {
            Enabled = true,
            Region = "us-east-1",
            ParsingModelId = "anthropic.claude-3-5-haiku-20241022-v1:0",
            ParsingMaxTokens = 500,
            ParsingTemperature = 0.1f
        });

        _service = new BedrockQueryParserService(
            _clientMock.Object, _loggerMock.Object, _settings);
    }

    public void Dispose()
    {
        _service.Dispose();
    }

    [Fact]
    public async Task ParseAsync_WithValidInput_ReturnsParsedResult()
    {
        // Arrange
        var llmJson = @"{""material_family"":""cmu"",""width_inches"":8,""height_inches"":8,""length_inches"":16,""thickness_inches"":null,""diameter_inches"":null,""attributes"":{""color"":""gray""},""search_query"":""8 inch 20 cm 200 mm concrete masonry unit CMU gray"",""confidence"":0.95}";
        SetupConverseResponse(llmJson);

        // Act
        var result = await _service.ParseAsync("8 inch concrete masonry unit gray");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("cmu", result.MaterialFamily);
        Assert.Equal(8, result.TechnicalSpecs.WidthInches);
        Assert.Equal(8, result.TechnicalSpecs.HeightInches);
        Assert.Equal(16, result.TechnicalSpecs.LengthInches);
        Assert.Contains("gray", result.Attributes.Values);
        Assert.Contains("CMU", result.SearchQuery);
        Assert.Equal(0.95f, result.Confidence);
    }

    [Fact]
    public async Task ParseAsync_SendsCorrectConverseRequest()
    {
        // Arrange
        ConverseRequest? capturedRequest = null;

        _clientMock.Setup(c => c.ConverseAsync(
                It.IsAny<ConverseRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<ConverseRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(CreateConverseResponse(@"{""material_family"":""rebar"",""search_query"":""rebar"",""confidence"":0.9}"));

        // Act
        await _service.ParseAsync("#5 rebar grade 60");

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Equal("anthropic.claude-3-5-haiku-20241022-v1:0", capturedRequest!.ModelId);

        // System message should contain the shared prompt
        Assert.Single(capturedRequest.System);
        Assert.Contains("construction materials parser", capturedRequest.System[0].Text);

        // User message should contain the BOM input
        Assert.Single(capturedRequest.Messages);
        Assert.Equal(ConversationRole.User, capturedRequest.Messages[0].Role);
        Assert.Contains("#5 rebar grade 60", capturedRequest.Messages[0].Content[0].Text);

        // Inference config
        Assert.Equal(500, capturedRequest.InferenceConfig.MaxTokens);
        Assert.Equal(0.1f, capturedRequest.InferenceConfig.Temperature);
    }

    [Fact]
    public async Task ParseAsync_WithEmptyInput_ReturnsFailure()
    {
        // Act
        var result = await _service.ParseAsync("");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("null or empty", result.ErrorMessage);
    }

    [Fact]
    public async Task ParseAsync_WithNullInput_ReturnsFailure()
    {
        // Act
        var result = await _service.ParseAsync(null!);

        // Assert
        Assert.False(result.Success);
    }

    [Fact]
    public async Task ParseAsync_EmptyResponse_ReturnsFailure()
    {
        // Arrange — response with no text
        SetupConverseResponse("");

        // Act
        var result = await _service.ParseAsync("some material");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("empty response", result.ErrorMessage);
    }

    [Fact]
    public async Task ParseAsync_InvalidJson_ReturnsFailureWithFallbackSearchQuery()
    {
        // Arrange — LLM returns non-JSON text
        SetupConverseResponse("I don't understand the input");

        // Act
        var result = await _service.ParseAsync("mystery material");

        // Assert
        Assert.False(result.Success);
        Assert.Equal("mystery material", result.SearchQuery); // Falls back to original input
    }

    [Fact]
    public async Task ParseAsync_ThrottlingException_ReturnsFailure()
    {
        // Arrange
        _clientMock.Setup(c => c.ConverseAsync(
                It.IsAny<ConverseRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ThrottlingException("Rate limit exceeded"));

        // Act
        var result = await _service.ParseAsync("8 inch block");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("throttled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ParseAsync_ValidationException_ReturnsFailure()
    {
        // Arrange
        _clientMock.Setup(c => c.ConverseAsync(
                It.IsAny<ConverseRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Amazon.BedrockRuntime.Model.ValidationException("Invalid model"));

        // Act
        var result = await _service.ParseAsync("8 inch block");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("validation", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ParseAsync_LlmResponseWithExtraText_ExtractsJson()
    {
        // Arrange — LLM wraps JSON with explanation text
        var response = @"Here is the parsed result:
{""material_family"":""lumber"",""width_inches"":1.5,""height_inches"":3.5,""length_inches"":96,""attributes"":{""nominal"":""2x4""},""search_query"":""2x4 lumber wood timber"",""confidence"":0.9}
This represents a standard 2x4 board.";
        SetupConverseResponse(response);

        // Act
        var result = await _service.ParseAsync("2x4 lumber 8ft");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("lumber", result.MaterialFamily);
        Assert.Equal(1.5, result.TechnicalSpecs.WidthInches);
    }

    [Fact]
    public async Task IsAvailableAsync_SuccessfulPing_ReturnsTrue()
    {
        // Arrange
        _clientMock.Setup(c => c.ConverseAsync(
                It.IsAny<ConverseRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateConverseResponse("pong"));

        // Act
        var available = await _service.IsAvailableAsync();

        // Assert
        Assert.True(available);
    }

    [Fact]
    public async Task IsAvailableAsync_Exception_ReturnsFalse()
    {
        // Arrange
        _clientMock.Setup(c => c.ConverseAsync(
                It.IsAny<ConverseRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Network error"));

        // Act
        var available = await _service.IsAvailableAsync();

        // Assert
        Assert.False(available);
    }

    // ─── Helpers ───

    private void SetupConverseResponse(string outputText)
    {
        _clientMock.Setup(c => c.ConverseAsync(
                It.IsAny<ConverseRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateConverseResponse(outputText));
    }

    private static ConverseResponse CreateConverseResponse(string outputText)
    {
        var response = new ConverseResponse
        {
            Output = new ConverseOutput
            {
                Message = new Message
                {
                    Role = ConversationRole.Assistant,
                    Content = new List<ContentBlock>
                    {
                        new() { Text = outputText }
                    }
                }
            },
            StopReason = StopReason.End_turn,
            Usage = new TokenUsage
            {
                InputTokens = 100,
                OutputTokens = 50,
                TotalTokens = 150
            }
        };
        return response;
    }
}
