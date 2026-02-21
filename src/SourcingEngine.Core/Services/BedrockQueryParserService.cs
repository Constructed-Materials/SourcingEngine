using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SourcingEngine.Core.Configuration;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Query parsing service using AWS Bedrock's Converse API for BOM line item analysis.
/// Uses the model-agnostic Converse API so the underlying model can be changed via config
/// (Claude, Nova, Llama, Mistral, etc.) without code changes.
/// Uses the default AWS credential chain (IAM Role on ECS/Lambda, env vars, or profile).
/// </summary>
public class BedrockQueryParserService : IQueryParserService, IDisposable
{
    private readonly IAmazonBedrockRuntime _client;
    private readonly ILogger<BedrockQueryParserService> _logger;
    private readonly BedrockSettings _settings;
    private readonly bool _ownsClient;

    public BedrockQueryParserService(
        ILogger<BedrockQueryParserService> logger,
        IOptions<BedrockSettings> settings)
        : this(null, logger, settings)
    {
    }

    /// <summary>
    /// Constructor with injectable IAmazonBedrockRuntime for testing.
    /// </summary>
    public BedrockQueryParserService(
        IAmazonBedrockRuntime? client,
        ILogger<BedrockQueryParserService> logger,
        IOptions<BedrockSettings> settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));

        if (client != null)
        {
            _client = client;
            _ownsClient = false;
        }
        else
        {
            _client = new AmazonBedrockRuntimeClient(RegionEndpoint.GetBySystemName(_settings.Region));
            _ownsClient = true;
        }
    }

    public async Task<ParsedBomQuery> ParseAsync(string bomLineItem, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bomLineItem))
        {
            return new ParsedBomQuery
            {
                Success = false,
                ErrorMessage = "Input cannot be null or empty",
                OriginalInput = bomLineItem ?? string.Empty
            };
        }

        _logger.LogDebug("Parsing BOM line item via Bedrock ({Model}): {Input}",
            _settings.ParsingModelId, bomLineItem);

        try
        {
            // Build the user prompt (examples + input)
            var userPrompt = $"{QueryParserPrompts.FewShotExamples}\nINPUT: \"{bomLineItem}\"\nOUTPUT:";

            var request = new ConverseRequest
            {
                ModelId = _settings.ParsingModelId,
                System = new List<SystemContentBlock>
                {
                    new() { Text = QueryParserPrompts.SystemPrompt }
                },
                Messages = new List<Message>
                {
                    new()
                    {
                        Role = ConversationRole.User,
                        Content = new List<ContentBlock>
                        {
                            new() { Text = userPrompt }
                        }
                    }
                },
                InferenceConfig = new InferenceConfiguration
                {
                    MaxTokens = _settings.ParsingMaxTokens,
                    Temperature = _settings.ParsingTemperature
                }
            };

            var response = await _client.ConverseAsync(request, cancellationToken);

            // Extract text from response
            var outputText = response.Output?.Message?.Content
                ?.FirstOrDefault()?.Text;

            if (string.IsNullOrWhiteSpace(outputText))
            {
                _logger.LogWarning("Bedrock returned empty response for: {Input}", bomLineItem);
                return new ParsedBomQuery
                {
                    Success = false,
                    ErrorMessage = "Bedrock returned empty response",
                    OriginalInput = bomLineItem
                };
            }

            _logger.LogDebug("Bedrock raw response ({StopReason}, {InputTokens}in/{OutputTokens}out): {Response}",
                response.StopReason?.Value,
                response.Usage?.InputTokens,
                response.Usage?.OutputTokens,
                outputText);

            // Use shared response parser
            return QueryParserResponseParser.Parse(outputText, bomLineItem, _logger);
        }
        catch (Amazon.BedrockRuntime.Model.ValidationException ex)
        {
            _logger.LogError(ex, "Bedrock validation error for model {ModelId}", _settings.ParsingModelId);
            return new ParsedBomQuery
            {
                Success = false,
                ErrorMessage = $"Bedrock validation error: {ex.Message}",
                OriginalInput = bomLineItem
            };
        }
        catch (Amazon.BedrockRuntime.Model.ThrottlingException ex)
        {
            _logger.LogWarning(ex, "Bedrock throttled for model {ModelId}", _settings.ParsingModelId);
            return new ParsedBomQuery
            {
                Success = false,
                ErrorMessage = $"Bedrock throttled: {ex.Message}",
                OriginalInput = bomLineItem
            };
        }
        catch (Amazon.BedrockRuntime.Model.ModelNotReadyException ex)
        {
            _logger.LogWarning(ex, "Bedrock model not ready: {ModelId}", _settings.ParsingModelId);
            return new ParsedBomQuery
            {
                Success = false,
                ErrorMessage = $"Model not ready: {ex.Message}",
                OriginalInput = bomLineItem
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to parse BOM line item via Bedrock: {Input}", bomLineItem);
            return new ParsedBomQuery
            {
                Success = false,
                ErrorMessage = ex.Message,
                OriginalInput = bomLineItem
            };
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Lightweight probe: send a minimal Converse request
            var request = new ConverseRequest
            {
                ModelId = _settings.ParsingModelId,
                Messages = new List<Message>
                {
                    new()
                    {
                        Role = ConversationRole.User,
                        Content = new List<ContentBlock>
                        {
                            new() { Text = "ping" }
                        }
                    }
                },
                InferenceConfig = new InferenceConfiguration
                {
                    MaxTokens = 1,
                    Temperature = 0f
                }
            };

            var response = await _client.ConverseAsync(request, cancellationToken);
            return response.Output?.Message != null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Bedrock availability check failed for model {ModelId}", _settings.ParsingModelId);
            return false;
        }
    }

    public void Dispose()
    {
        if (_ownsClient && _client is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
