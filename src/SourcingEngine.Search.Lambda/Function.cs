using System.Text;
using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.MQEvents;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SourcingEngine.Common.Models;
using SourcingEngine.Core.Models;
using SourcingEngine.Core.Services;
using SourcingEngine.Data;
using SourcingEngine.Search.Lambda.Configuration;
using SourcingEngine.Search.Lambda.Services;

// Assembly attribute to tell Lambda about the serializer
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace SourcingEngine.Search.Lambda;

/// <summary>
/// AWS Lambda function handler triggered by Amazon MQ (RabbitMQ).
/// Consumes BOM extraction result messages from bom-extraction-result-queue,
/// runs the SourcingEngine search pipeline for each BOM item, and publishes
/// results to the sourcing.engine exchange — split between results and zero-results
/// routing keys.
/// </summary>
public class Function
{
    private readonly ISearchOrchestrator _orchestrator;
    private readonly IRabbitMqSearchResultPublisher _publisher;
    private readonly SearchLambdaSettings _lambdaSettings;
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
        _orchestrator = services.GetRequiredService<ISearchOrchestrator>();
        _publisher = services.GetRequiredService<IRabbitMqSearchResultPublisher>();
        _lambdaSettings = services.GetRequiredService<IOptions<SearchLambdaSettings>>().Value;
        _logger = services.GetRequiredService<ILogger<Function>>();
    }

    /// <summary>
    /// Constructor for dependency injection — used by tests and local runner.
    /// </summary>
    internal Function(
        ISearchOrchestrator orchestrator,
        IRabbitMqSearchResultPublisher publisher,
        SearchLambdaSettings lambdaSettings,
        ILogger<Function> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _lambdaSettings = lambdaSettings ?? throw new ArgumentNullException(nameof(lambdaSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Lambda entry point. Invoked by the Amazon MQ event source mapping.
    /// Each RabbitMQEvent may contain one or more messages from bom-extraction-result-queue.
    /// Batch size is configured to 1 in the event source mapping.
    /// </summary>
    public async Task FunctionHandler(RabbitMQEvent mqEvent, ILambdaContext context)
    {
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
        if (string.IsNullOrEmpty(message.Data))
            throw new InvalidOperationException("Message data is null or empty");

        var bodyBytes = Convert.FromBase64String(message.Data);
        var bodyJson = Encoding.UTF8.GetString(bodyBytes);

        ExtractionResultMessage? extractionResult;
        try
        {
            extractionResult = JsonSerializer.Deserialize<ExtractionResultMessage>(bodyJson, CamelCaseOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize message body: {Preview}",
                bodyJson[..Math.Min(bodyJson.Length, 500)]);
            throw;
        }

        if (extractionResult == null || string.IsNullOrEmpty(extractionResult.TraceId))
        {
            _logger.LogError("Invalid extraction result message: null or missing traceId");
            throw new InvalidOperationException("Invalid extraction result: null or missing traceId");
        }

        _logger.LogInformation(
            "trace_id={TraceId} Processing extraction result projectId={ProjectId} file={SourceFile} items={ItemCount}",
            extractionResult.TraceId, extractionResult.ProjectId, extractionResult.SourceFile, extractionResult.Items.Count);

        // Run the search pipeline
        var request = new SourcingRequest { ExtractionResult = extractionResult };
        var sourcingResult = await _orchestrator.SearchAsync(request);

        _logger.LogInformation(
            "trace_id={TraceId} Search completed: {TotalMatches} total matches across {ItemCount} items in {ElapsedMs}ms",
            extractionResult.TraceId, sourcingResult.TotalMatches,
            sourcingResult.Items.Count, sourcingResult.TotalExecutionTimeMs);

        // Split results into items-with-matches and items-with-zero-matches
        var withMatches = sourcingResult.Items.Where(i => i.SearchResult.MatchCount > 0).ToList();
        var zeroMatches = sourcingResult.Items.Where(i => i.SearchResult.MatchCount == 0).ToList();

        // Publish items with matches to the results queue
        if (withMatches.Count > 0)
        {
            var resultMessage = BuildResultMessage(extractionResult, sourcingResult, withMatches);
            await _publisher.PublishResultAsync(resultMessage, extractionResult.TraceId);
        }

        // Publish items with zero matches to the zero-results queue
        if (zeroMatches.Count > 0)
        {
            var zeroResultMessage = BuildZeroResultsMessage(extractionResult, sourcingResult, zeroMatches);
            await _publisher.PublishZeroResultsAsync(zeroResultMessage, extractionResult.TraceId);
        }

        _logger.LogInformation(
            "trace_id={TraceId} Published: {WithMatches} item(s) to results, {ZeroMatches} item(s) to zero-results",
            extractionResult.TraceId, withMatches.Count, zeroMatches.Count);
    }

    private static SourcingResultMessage BuildResultMessage(
        ExtractionResultMessage extraction,
        SourcingResult sourcingResult,
        List<BomItemSearchResult> withMatches)
    {
        return new SourcingResultMessage
        {
            TraceId = extraction.TraceId,
            ProjectId = extraction.ProjectId,
            SourceFile = extraction.SourceFile,
            TotalMatches = withMatches.Sum(i => i.SearchResult.MatchCount),
            TotalExecutionTimeMs = sourcingResult.TotalExecutionTimeMs,
            Warnings = sourcingResult.Warnings,
            Items = withMatches.Select(item => new SourcingResultItem
            {
                BomItem = item.BomItemName,
                Spec = item.Spec,
                Quantity = item.Quantity,
                MatchCount = item.SearchResult.MatchCount,
                FamilyLabel = item.SearchResult.FamilyLabel,
                CsiCode = item.SearchResult.CsiCode,
                ExecutionTimeMs = item.SearchResult.ExecutionTimeMs,
                Warnings = item.SearchResult.Warnings,
                Matches = item.SearchResult.Matches.Select(m => new ProductMatchDto
                {
                    ProductId = m.ProductId,
                    Vendor = m.Vendor,
                    ModelName = m.ModelName,
                    CsiCode = m.CsiCode,
                    Description = m.Description,
                    UseCases = m.UseCases,
                    TechnicalSpecs = m.TechnicalSpecs,
                    SemanticScore = m.SemanticScore,
                    FinalScore = m.FinalScore,
                }).ToList(),
            }).ToList(),
        };
    }

    private static SourcingZeroResultsMessage BuildZeroResultsMessage(
        ExtractionResultMessage extraction,
        SourcingResult sourcingResult,
        List<BomItemSearchResult> zeroMatches)
    {
        return new SourcingZeroResultsMessage
        {
            TraceId = extraction.TraceId,
            ProjectId = extraction.ProjectId,
            SourceFile = extraction.SourceFile,
            Warnings = sourcingResult.Warnings,
            Items = zeroMatches.Select(item => new ZeroResultItem
            {
                BomItem = item.BomItemName,
                Spec = item.Spec,
                Quantity = item.Quantity,
                Warnings = item.SearchResult.Warnings,
            }).ToList(),
        };
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

        // Resolve database connection string from Secrets Manager (if ARN provided)
        ResolveDatabaseSecret(configuration);

        var services = new ServiceCollection();

        // Configuration
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<SearchLambdaSettings>(configuration.GetSection(SearchLambdaSettings.SectionName));

        // Logging
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
            builder.AddConsole();
        });

        // SourcingEngine full stack (Core + Data: DB, Bedrock, search strategy, orchestrator)
        services.AddSourcingEngine(configuration);

        // AWS SDK clients (singleton — reused across invocations)
        services.AddSingleton<IAmazonSecretsManager>(new AmazonSecretsManagerClient());

        // RabbitMQ publisher (singleton — connection reused across invocations)
        services.AddSingleton<IRabbitMqSearchResultPublisher, RabbitMqSearchResultPublisher>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Resolves the database connection string from AWS Secrets Manager
    /// and injects it into the configuration before DI binding.
    /// Falls back to existing Database:ConnectionString if no secret ARN is configured.
    /// </summary>
    private static void ResolveDatabaseSecret(IConfigurationRoot configuration)
    {
        var secretArn = configuration.GetValue<string>("Lambda:DatabaseSecretArn");
        if (string.IsNullOrEmpty(secretArn))
        {
            return; // local dev — use Database:ConnectionString directly
        }

        using var client = new AmazonSecretsManagerClient();
        var response = client.GetSecretValueAsync(new GetSecretValueRequest
        {
            SecretId = secretArn,
        }).GetAwaiter().GetResult();

        var secret = JsonSerializer.Deserialize<DatabaseSecret>(response.SecretString)
            ?? throw new InvalidOperationException(
                $"Failed to deserialize database secret from {secretArn}");

        if (string.IsNullOrEmpty(secret.ConnectionString))
        {
            throw new InvalidOperationException(
                $"Database secret {secretArn} does not contain a 'connectionString' field");
        }

        // Override the Database:ConnectionString value so AddSourcingEngine() picks it up
        configuration["Database:ConnectionString"] = secret.ConnectionString;
    }

    /// <summary>JSON shape stored in Secrets Manager for the database connection string.</summary>
    private sealed class DatabaseSecret
    {
        [System.Text.Json.Serialization.JsonPropertyName("connectionString")]
        public string ConnectionString { get; set; } = string.Empty;
    }
}
