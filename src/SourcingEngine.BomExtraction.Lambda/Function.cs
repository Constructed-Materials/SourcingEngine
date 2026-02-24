using System.Text;
using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.MQEvents;
using Amazon.S3;
using Amazon.SecretsManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SourcingEngine.BomExtraction.Configuration;
using SourcingEngine.BomExtraction.Lambda.Configuration;
using SourcingEngine.BomExtraction.Lambda.Models;
using SourcingEngine.BomExtraction.Lambda.Services;
using SourcingEngine.BomExtraction.Parsing;
using SourcingEngine.BomExtraction.Services;

// Assembly attribute to tell Lambda about the serializer
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace SourcingEngine.BomExtraction.Lambda;

/// <summary>
/// AWS Lambda function handler triggered by Amazon MQ (RabbitMQ).
/// Consumes extraction request messages, downloads BOM files,
/// runs Bedrock-based extraction, and publishes results back to the broker.
/// </summary>
public class Function
{
    private readonly IBomExtractionService _extractionService;
    private readonly IRemoteFileFetcher _fileFetcher;
    private readonly IRabbitMqResultPublisher _publisher;
    private readonly LambdaSettings _lambdaSettings;
    private readonly ILogger<Function> _logger;
    private bool _publisherInitialized;

    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Default constructor — used by Lambda runtime.
    /// Initializes DI container with all services following AWS best practice
    /// of creating SDK clients outside the handler.
    /// </summary>
    public Function()
    {
        var services = ConfigureServices();
        _extractionService = services.GetRequiredService<IBomExtractionService>();
        _fileFetcher = services.GetRequiredService<IRemoteFileFetcher>();
        _publisher = services.GetRequiredService<IRabbitMqResultPublisher>();
        _lambdaSettings = services.GetRequiredService<IOptions<LambdaSettings>>().Value;
        _logger = services.GetRequiredService<ILogger<Function>>();
    }

    /// <summary>
    /// Constructor for dependency injection — used by tests and local runner.
    /// </summary>
    internal Function(
        IBomExtractionService extractionService,
        IRemoteFileFetcher fileFetcher,
        IRabbitMqResultPublisher publisher,
        LambdaSettings lambdaSettings,
        ILogger<Function> logger)
    {
        _extractionService = extractionService ?? throw new ArgumentNullException(nameof(extractionService));
        _fileFetcher = fileFetcher ?? throw new ArgumentNullException(nameof(fileFetcher));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _lambdaSettings = lambdaSettings ?? throw new ArgumentNullException(nameof(lambdaSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Lambda entry point. Invoked by the Amazon MQ event source mapping.
    /// Each RabbitMQEvent may contain one or more messages from bom-extraction-queue.
    /// Batch size is configured to 1 in the event source mapping.
    /// </summary>
    public async Task FunctionHandler(RabbitMQEvent mqEvent, ILambdaContext context)
    {
        // Overload that delegates — keeps both signatures available
        await FunctionHandlerCore(mqEvent, context);
    }

    internal async Task FunctionHandlerCore(RabbitMQEvent mqEvent, ILambdaContext context)
    {
        _logger.LogInformation("Received RabbitMQ event with {QueueCount} queue(s), RequestId={RequestId}",
            mqEvent.RmqMessagesByQueue?.Count ?? 0, context.AwsRequestId);

        // Lazy-initialize publisher on first invocation (reused on warm starts)
        if (!_publisherInitialized)
        {
            await _publisher.InitializeAsync();
            _publisherInitialized = true;
        }

        if (mqEvent.RmqMessagesByQueue == null || mqEvent.RmqMessagesByQueue.Count == 0)
        {
            _logger.LogWarning("No messages in event — returning");
            return;
        }

        foreach (var (queueName, messages) in mqEvent.RmqMessagesByQueue)
        {
            _logger.LogInformation("Processing {Count} message(s) from queue {Queue}", messages.Count, queueName);

            foreach (var message in messages)
            {
                await ProcessSingleMessageAsync(message, context);
            }
        }
    }

    internal async Task ProcessSingleMessageAsync(RabbitMQEvent.RabbitMQMessage message, ILambdaContext context)
    {
        // message.Data is a base64-encoded string; Lambda serializer decodes it to string
        if (string.IsNullOrEmpty(message.Data))
            throw new InvalidOperationException("Message data is null or empty");

        var bodyBytes = Convert.FromBase64String(message.Data);
        var bodyJson = Encoding.UTF8.GetString(bodyBytes);

        ExtractionRequestMessage? request;
        try
        {
            request = JsonSerializer.Deserialize<ExtractionRequestMessage>(bodyJson, CamelCaseOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize message body: {Preview}",
                bodyJson[..Math.Min(bodyJson.Length, 500)]);
            // Let exception propagate → Lambda retries → eventually message TTL → DLX
            throw;
        }

        if (request == null || string.IsNullOrEmpty(request.TraceId))
        {
            _logger.LogError("Invalid request message: null or missing traceId");
            throw new InvalidOperationException("Invalid extraction request: null or missing traceId");
        }

        _logger.LogInformation("trace_id={TraceId} Processing extraction request projectId={ProjectId} files={FileCount}",
            request.TraceId, request.ProjectId, request.BomFiles.Count);

        // Create a per-invocation temp directory
        var tempDir = Path.Combine(_lambdaSettings.TempDirectory, request.TraceId);
        Directory.CreateDirectory(tempDir);

        try
        {
            await ProcessRequestFilesAsync(request, tempDir, context);
        }
        finally
        {
            // Clean up temp files to free /tmp space for future warm invocations
            CleanupTempDirectory(tempDir);
        }
    }

    private async Task ProcessRequestFilesAsync(
        ExtractionRequestMessage request,
        string tempDir,
        ILambdaContext context)
    {
        for (var i = 0; i < request.BomFiles.Count; i++)
        {
            var fileRef = request.BomFiles[i];
            var localPath = Path.Combine(tempDir, $"{i:D3}_{fileRef.FileName}");

            _logger.LogInformation("trace_id={TraceId} Downloading file {Index}/{Total}: {FileName} from {Url}",
                request.TraceId, i + 1, request.BomFiles.Count, fileRef.FileName, fileRef.Url);

            // Download the BOM file
            await _fileFetcher.DownloadToPathAsync(fileRef.Url, localPath);

            // Run extraction via Bedrock Converse API
            _logger.LogInformation("trace_id={TraceId} Extracting BOM from {FileName}",
                request.TraceId, fileRef.FileName);

            var extractionResult = await _extractionService.ExtractAsync(localPath);

            // Build result message matching the Python contract
            var resultMessage = new ExtractionResultMessage
            {
                TraceId = request.TraceId,
                ProjectId = request.ProjectId,
                SourceFile = fileRef.FileName,
                SourceUrl = fileRef.Url,
                ItemCount = extractionResult.ItemCount,
                Items = extractionResult.Items,
                Warnings = extractionResult.Warnings,
                ModelUsed = extractionResult.ModelUsed,
                InputTokens = extractionResult.InputTokens,
                OutputTokens = extractionResult.OutputTokens,
            };

            // Publish to result queue
            await _publisher.PublishResultAsync(resultMessage, request.TraceId);

            _logger.LogInformation(
                "trace_id={TraceId} Published result for {FileName}: {ItemCount} items ({InputTokens}in/{OutputTokens}out)",
                request.TraceId, fileRef.FileName, extractionResult.ItemCount,
                extractionResult.InputTokens, extractionResult.OutputTokens);
        }

        _logger.LogInformation("trace_id={TraceId} Completed all {Count} files for projectId={ProjectId}",
            request.TraceId, request.BomFiles.Count, request.ProjectId);
    }

    private void CleanupTempDirectory(string tempDir)
    {
        try
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
                _logger.LogDebug("Cleaned up temp directory: {TempDir}", tempDir);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up temp directory: {TempDir}", tempDir);
        }
    }

    /// <summary>
    /// Configures the DI service provider.
    /// Called once during Lambda cold start — SDK clients and connections
    /// are initialized here and reused across warm invocations.
    /// </summary>
    internal static ServiceProvider ConfigureServices()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();

        // Configuration
        services.Configure<BomExtractionSettings>(configuration.GetSection(BomExtractionSettings.SectionName));
        services.Configure<LambdaSettings>(configuration.GetSection(LambdaSettings.SectionName));

        // Logging
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
            builder.AddConsole();
        });

        // AWS SDK clients (singleton — reused across invocations)
        services.AddSingleton<IAmazonS3>(new AmazonS3Client());
        services.AddSingleton<IAmazonSecretsManager>(new AmazonSecretsManagerClient());

        // HTTP client for presigned URL downloads
        services.AddHttpClient<IRemoteFileFetcher, RemoteFileFetcher>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("bom-extraction-lambda/1.0");
        });

        // Core extraction service
        services.AddSingleton<JsonResponseParser>();
        services.AddSingleton<IBomExtractionService, BomExtractionService>();

        // RabbitMQ publisher (singleton — connection reused across invocations)
        services.AddSingleton<IRabbitMqResultPublisher, RabbitMqResultPublisher>();

        return services.BuildServiceProvider();
    }
}
