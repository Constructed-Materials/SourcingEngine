using System.Text;
using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using SourcingEngine.Common.Models;
using SourcingEngine.Search.Lambda.Configuration;

namespace SourcingEngine.Search.Lambda.Services;

/// <summary>
/// Publishes sourcing search results back to the RabbitMQ broker.
/// Supports two routing keys: one for items with matches, another for zero-result items.
/// Connection is established once and reused across warm Lambda invocations.
/// </summary>
public interface IRabbitMqSearchResultPublisher : IAsyncDisposable
{
    /// <summary>Publish search result message (items with matches) to the result exchange.</summary>
    Task PublishResultAsync(SourcingResultMessage result, string traceId, CancellationToken ct = default);

    /// <summary>Publish zero-result message (items with no matches) to the result exchange.</summary>
    Task PublishZeroResultsAsync(SourcingZeroResultsMessage result, string traceId, CancellationToken ct = default);

    /// <summary>Ensure the connection is established (called once during init).</summary>
    Task InitializeAsync(CancellationToken ct = default);
}

public class RabbitMqSearchResultPublisher : IRabbitMqSearchResultPublisher
{
    private readonly SearchLambdaSettings _settings;
    private readonly IAmazonSecretsManager _secretsManager;
    private readonly ILogger<RabbitMqSearchResultPublisher> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    private IConnection? _connection;
    private IChannel? _channel;
    private bool _initialized;

    public RabbitMqSearchResultPublisher(
        IOptions<SearchLambdaSettings> settings,
        IAmazonSecretsManager secretsManager,
        ILogger<RabbitMqSearchResultPublisher> logger)
    {
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _secretsManager = secretsManager ?? throw new ArgumentNullException(nameof(secretsManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null, // We use explicit JsonPropertyName attributes
            WriteIndented = false,
        };
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized && IsConnectionHealthy()) return;

        await ConnectAsync(ct);
    }

    public async Task PublishResultAsync(SourcingResultMessage result, string traceId, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);

        var body = JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Headers = new Dictionary<string, object?>
            {
                ["trace_id"] = traceId,
                ["message_type"] = "sourcing_result",
            },
        };

        await _channel!.BasicPublishAsync(
            exchange: _settings.ResultExchange,
            routingKey: _settings.ResultRoutingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: ct);

        _logger.LogInformation(
            "Published search results for trace_id={TraceId} sourceFile={SourceFile} items={ItemCount} totalMatches={TotalMatches}",
            traceId, result.SourceFile, result.Items.Count, result.TotalMatches);
    }

    public async Task PublishZeroResultsAsync(SourcingZeroResultsMessage result, string traceId, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);

        var body = JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Headers = new Dictionary<string, object?>
            {
                ["trace_id"] = traceId,
                ["message_type"] = "sourcing_zero_results",
            },
        };

        await _channel!.BasicPublishAsync(
            exchange: _settings.ResultExchange,
            routingKey: _settings.ZeroResultRoutingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: ct);

        _logger.LogInformation(
            "Published zero-results for trace_id={TraceId} sourceFile={SourceFile} zeroResultItems={ItemCount}",
            traceId, result.SourceFile, result.Items.Count);
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (!_initialized || !IsConnectionHealthy())
        {
            _logger.LogWarning("RabbitMQ connection is not healthy — reconnecting before publish");
            await ConnectAsync(ct);
        }
    }

    private bool IsConnectionHealthy()
    {
        return _connection is { IsOpen: true } && _channel is { IsOpen: true };
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        // Dispose any stale connection/channel first
        await DisposeConnectionAsync();

        var (username, password) = await ResolveBrokerCredentialsAsync(ct);

        var factory = new ConnectionFactory
        {
            HostName = _settings.BrokerHost,
            Port = _settings.BrokerPort,
            UserName = username,
            Password = password,
            VirtualHost = "/",
            Ssl = new SslOption
            {
                Enabled = _settings.BrokerUseSsl,
                ServerName = _settings.BrokerHost,
            },
            // Lambda best practice: fast timeouts for no-retry scenarios
            RequestedConnectionTimeout = TimeSpan.FromSeconds(10),
            SocketReadTimeout = TimeSpan.FromSeconds(10),
            SocketWriteTimeout = TimeSpan.FromSeconds(10),
        };

        _logger.LogInformation("Connecting to RabbitMQ broker at {Host}:{Port}", _settings.BrokerHost, _settings.BrokerPort);

        _connection = await factory.CreateConnectionAsync(ct);
        _channel = await _connection.CreateChannelAsync(cancellationToken: ct);

        // Declare the exchange idempotently in case it doesn't exist
        await _channel.ExchangeDeclareAsync(
            exchange: _settings.ResultExchange,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: ct);

        _initialized = true;
        _logger.LogInformation("RabbitMQ connection established successfully");
    }

    private async Task DisposeConnectionAsync()
    {
        try
        {
            if (_channel is { IsOpen: true })
                await _channel.CloseAsync();
            _channel?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ignoring error closing stale channel");
        }

        try
        {
            if (_connection is { IsOpen: true })
                await _connection.CloseAsync();
            _connection?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ignoring error closing stale connection");
        }

        _channel = null;
        _connection = null;
        _initialized = false;
    }

    private async Task<(string username, string password)> ResolveBrokerCredentialsAsync(CancellationToken ct)
    {
        // For local development: use direct credentials from config
        if (string.IsNullOrEmpty(_settings.BrokerSecretArn))
        {
            if (!string.IsNullOrEmpty(_settings.BrokerUsername))
            {
                _logger.LogDebug("Using direct broker credentials from configuration (local dev mode)");
                return (_settings.BrokerUsername, _settings.BrokerPassword);
            }
            throw new InvalidOperationException(
                "No broker credentials configured. Set BrokerSecretArn or BrokerUsername/BrokerPassword.");
        }

        _logger.LogDebug("Retrieving broker credentials from Secrets Manager: {SecretArn}", _settings.BrokerSecretArn);

        var response = await _secretsManager.GetSecretValueAsync(
            new GetSecretValueRequest { SecretId = _settings.BrokerSecretArn }, ct);

        var secret = JsonSerializer.Deserialize<BrokerSecret>(response.SecretString)
            ?? throw new InvalidOperationException("Failed to deserialize broker secret");

        return (secret.Username, secret.Password);
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeConnectionAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>Expected shape of the Secrets Manager JSON for broker credentials.</summary>
    private class BrokerSecret
    {
        [System.Text.Json.Serialization.JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;
    }
}
