using System.Text;
using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using SourcingEngine.BomExtraction.Lambda.Configuration;
using SourcingEngine.Common.Models;

namespace SourcingEngine.BomExtraction.Lambda.Services;

/// <summary>
/// Publishes extraction result messages back to the RabbitMQ broker.
/// Connection is established once and reused across warm Lambda invocations.
/// </summary>
public interface IRabbitMqResultPublisher : IAsyncDisposable
{
    /// <summary>Publish a result message to the configured result exchange.</summary>
    Task PublishResultAsync(ExtractionResultMessage result, string traceId, CancellationToken ct = default);

    /// <summary>Ensure the connection is established (called once during init).</summary>
    Task InitializeAsync(CancellationToken ct = default);
}

public class RabbitMqResultPublisher : IRabbitMqResultPublisher
{
    private readonly LambdaSettings _settings;
    private readonly IAmazonSecretsManager _secretsManager;
    private readonly ILogger<RabbitMqResultPublisher> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    private IConnection? _connection;
    private IChannel? _channel;
    private bool _initialized;

    public RabbitMqResultPublisher(
        IOptions<LambdaSettings> settings,
        IAmazonSecretsManager secretsManager,
        ILogger<RabbitMqResultPublisher> logger)
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

    public async Task PublishResultAsync(ExtractionResultMessage result, string traceId, CancellationToken ct = default)
    {
        // Reconnect if the connection has gone stale (idle timeout, broker restart, etc.)
        if (!_initialized || !IsConnectionHealthy())
        {
            _logger.LogWarning("RabbitMQ connection is not healthy — reconnecting before publish");
            await ConnectAsync(ct);
        }

        var body = JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Headers = new Dictionary<string, object?>
            {
                ["trace_id"] = traceId,
            },
        };

        await _channel.BasicPublishAsync(
            exchange: _settings.ResultExchange,
            routingKey: _settings.ResultRoutingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: ct);

        _logger.LogInformation(
            "Published result for trace_id={TraceId} sourceFile={SourceFile} itemCount={ItemCount}",
            traceId, result.SourceFile, result.ItemCount);
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
                Enabled = true,
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
