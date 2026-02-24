using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SourcingEngine.BomExtraction.Configuration;
using SourcingEngine.BomExtraction.Models;
using SourcingEngine.BomExtraction.Parsing;
using SourcingEngine.BomExtraction.Prompts;

namespace SourcingEngine.BomExtraction.Services;

/// <summary>
/// Extracts BOM line items from documents by sending the original file
/// to AWS Bedrock via the Converse API's DocumentBlock.
/// No raw-text extraction or chunking — the model processes the file natively.
/// </summary>
public class BomExtractionService : IBomExtractionService, IDisposable
{
    private readonly IAmazonBedrockRuntime _client;
    private readonly ILogger<BomExtractionService> _logger;
    private readonly BomExtractionSettings _settings;
    private readonly JsonResponseParser _parser;
    private readonly bool _ownsClient;

    public BomExtractionService(
        ILogger<BomExtractionService> logger,
        IOptions<BomExtractionSettings> settings,
        JsonResponseParser parser)
        : this(null, logger, settings, parser)
    {
    }

    /// <summary>
    /// Constructor with injectable <see cref="IAmazonBedrockRuntime"/> for testing.
    /// When <paramref name="client"/> is null, a real client is created using config.
    /// </summary>
    public BomExtractionService(
        IAmazonBedrockRuntime? client,
        ILogger<BomExtractionService> logger,
        IOptions<BomExtractionSettings> settings,
        JsonResponseParser parser)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));

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

    /// <inheritdoc />
    public async Task<ExtractionResult> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"BOM file not found: {filePath}", filePath);

        var extension = Path.GetExtension(filePath);
        if (!SupportedFileFormatExtensions.TryFromExtension(extension, out var format))
        {
            throw new ArgumentException(
                $"Unsupported file format: '{extension}'. " +
                $"Supported: {string.Join(", ", SupportedFileFormatExtensions.AllSupportedExtensions)}",
                nameof(filePath));
        }

        _logger.LogInformation("Extracting BOM from {FilePath} (format: {Format}, model: {Model})",
            filePath, format, _settings.ModelId);

        var result = new ExtractionResult
        {
            SourceFile = filePath,
            ModelUsed = _settings.ModelId,
        };

        try
        {
            var fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            _logger.LogDebug("Read {Bytes} bytes from {FilePath}", fileBytes.Length, filePath);

            var request = BuildConverseRequest(fileBytes, format, filePath);
            var response = await _client.ConverseAsync(request, cancellationToken);

            var outputText = response.Output?.Message?.Content?.FirstOrDefault()?.Text;

            result.InputTokens = response.Usage?.InputTokens;
            result.OutputTokens = response.Usage?.OutputTokens;

            _logger.LogDebug(
                "Bedrock response ({StopReason}, {InputTokens}in/{OutputTokens}out): {ResponsePreview}",
                response.StopReason?.Value,
                response.Usage?.InputTokens,
                response.Usage?.OutputTokens,
                outputText?[..Math.Min(outputText.Length, 200)]);

            // Warn on truncation
            if (response.StopReason?.Value == "max_tokens")
            {
                result.Warnings.Add(
                    $"Response was truncated (max_tokens). " +
                    $"Used {response.Usage?.OutputTokens} output tokens. " +
                    $"Consider increasing MaxTokens (currently {_settings.MaxTokens}).");
                _logger.LogWarning("Bedrock response truncated at {MaxTokens} tokens for {FilePath}",
                    _settings.MaxTokens, filePath);
            }

            if (string.IsNullOrWhiteSpace(outputText))
            {
                result.Warnings.Add("Bedrock returned an empty response.");
                _logger.LogWarning("Bedrock returned empty response for {FilePath}", filePath);
                return result;
            }

            var items = _parser.Parse(outputText);
            result.Items = items;

            _logger.LogInformation("Extracted {Count} BOM line items from {FilePath}", items.Count, filePath);
        }
        catch (BomParsingException ex)
        {
            result.Warnings.Add($"JSON parsing failed: {ex.Message}");
            _logger.LogError(ex, "Failed to parse BOM extraction response for {FilePath}", filePath);
            throw;
        }
        catch (AmazonBedrockRuntimeException ex)
        {
            result.Warnings.Add($"Bedrock API error: {ex.Message}");
            _logger.LogError(ex, "Bedrock API error during extraction of {FilePath}", filePath);
            throw;
        }

        result.ExtractedAt = DateTime.UtcNow;
        return result;
    }

    /// <summary>
    /// Build the Converse API request with the document as a DocumentBlock.
    /// </summary>
    internal ConverseRequest BuildConverseRequest(byte[] fileBytes, SupportedFileFormat format, string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);

        return new ConverseRequest
        {
            ModelId = _settings.ModelId,
            System = new List<SystemContentBlock>
            {
                new() { Text = SystemPrompts.BomExtraction }
            },
            Messages = new List<Message>
            {
                new()
                {
                    Role = ConversationRole.User,
                    Content = new List<ContentBlock>
                    {
                        new()
                        {
                            Document = new DocumentBlock
                            {
                                Format = format.ToBedrockFormat(),
                                Name = fileName,
                                Source = new DocumentSource
                                {
                                    Bytes = new MemoryStream(fileBytes)
                                }
                            }
                        },
                        new() { Text = SystemPrompts.UserPrompt }
                    }
                }
            },
            InferenceConfig = new InferenceConfiguration
            {
                MaxTokens = _settings.MaxTokens,
                Temperature = _settings.Temperature,
            }
        };
    }

    public void Dispose()
    {
        if (_ownsClient && _client is IDisposable disposable)
        {
            disposable.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
