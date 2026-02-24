using System.Text;
using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.MQEvents;
using SourcingEngine.BomExtraction.Lambda;

/// <summary>
/// Local debugging harness that consumes from a local RabbitMQ queue
/// and invokes the Lambda handler, enabling full F5 debugging.
///
/// Usage:
///   1. Start local RabbitMQ:  docker compose -f local/docker-compose.local.yml up -d
///   2. Run this:              dotnet run --project . -- --local
///   3. Publish a message to bom-extraction-queue via RabbitMQ Management UI (localhost:15672)
///
/// The harness converts native AMQP messages into the RabbitMQEvent structure
/// that Lambda would receive, then calls Function.FunctionHandler().
/// </summary>
public static class LocalRunner
{
    public static async Task RunAsync(string[] args)
    {
        Console.WriteLine("=== BOM Extraction Lambda — Local Debug Runner ===");
        Console.WriteLine();

        // Check for --event flag to replay a saved event file
        var eventFileIdx = Array.IndexOf(args, "--event");
        if (eventFileIdx >= 0 && eventFileIdx + 1 < args.Length)
        {
            await ReplayEventFile(args[eventFileIdx + 1]);
            return;
        }

        // Otherwise, consume from local RabbitMQ
        await ConsumeFromLocalQueue();
    }

    /// <summary>
    /// Replay a saved RabbitMQEvent JSON file through the Lambda handler.
    /// Useful for reproducing specific scenarios.
    /// </summary>
    private static async Task ReplayEventFile(string eventFilePath)
    {
        Console.WriteLine($"Replaying event file: {eventFilePath}");
        Console.WriteLine();

        if (!File.Exists(eventFilePath))
        {
            Console.Error.WriteLine($"Event file not found: {eventFilePath}");
            Environment.Exit(1);
        }

        var json = await File.ReadAllTextAsync(eventFilePath);
        var mqEvent = JsonSerializer.Deserialize<RabbitMQEvent>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        if (mqEvent == null)
        {
            Console.Error.WriteLine("Failed to deserialize event file.");
            Environment.Exit(1);
        }

        // Set local-dev environment variables
        EnsureLocalEnvironment();

        var function = new Function();
        var context = new LocalLambdaContext();
        await function.FunctionHandler(mqEvent, context);

        Console.WriteLine();
        Console.WriteLine("=== Event replay complete ===");
    }

    /// <summary>
    /// Connect to local RabbitMQ broker and consume messages from
    /// bom-extraction-queue, converting each into a Lambda invocation.
    /// </summary>
    private static async Task ConsumeFromLocalQueue()
    {
        Console.WriteLine("Connecting to local RabbitMQ (localhost:5672)...");
        Console.WriteLine("Publish messages to 'bom-extraction-queue' via http://localhost:15672");
        Console.WriteLine("Press Ctrl+C to stop.");
        Console.WriteLine();

        EnsureLocalEnvironment();

        var factory = new RabbitMQ.Client.ConnectionFactory
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "guest",
            Password = "guest",
        };

        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        var function = new Function();
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var consumer = new RabbitMQ.Client.Events.AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            var body = ea.Body.ToArray();
            var bodyBase64 = Convert.ToBase64String(body);
            var bodyText = Encoding.UTF8.GetString(body);

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Received message (delivery tag {ea.DeliveryTag})");
            Console.WriteLine($"  Payload preview: {bodyText[..Math.Min(200, bodyText.Length)]}...");

            // Build a RabbitMQEvent matching the Lambda event format
            var mqEvent = new RabbitMQEvent
            {
                EventSource = "aws:rmq",
                RmqMessagesByQueue = new Dictionary<string, List<RabbitMQEvent.RabbitMQMessage>>
                {
                    ["bom-extraction-queue::/"] = new List<RabbitMQEvent.RabbitMQMessage>
                    {
                        new()
                        {
                            Data = bodyBase64,
                            BasicProperties = new RabbitMQEvent.BasicProperties
                            {
                                ContentType = ea.BasicProperties?.ContentType ?? "application/json",
                            },
                        }
                    }
                }
            };

            try
            {
                var context = new LocalLambdaContext();
                await function.FunctionHandler(mqEvent, context);
                await channel.BasicAckAsync(ea.DeliveryTag, false);
                Console.WriteLine($"  ✓ Processed successfully, ACK'd");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  ✗ Error: {ex.Message}");
                await channel.BasicNackAsync(ea.DeliveryTag, false, false);
                Console.Error.WriteLine($"  NACK'd (will route to DLX/poison queue)");
            }

            Console.WriteLine();
        };

        await channel.BasicConsumeAsync(
            queue: "bom-extraction-queue",
            autoAck: false,
            consumerTag: "",
            noLocal: false,
            exclusive: false,
            arguments: null,
            consumer: consumer);

        try { await Task.Delay(Timeout.Infinite, cts.Token); }
        catch (OperationCanceledException) { }

        Console.WriteLine();
        Console.WriteLine("=== Local runner stopped ===");
    }

    /// <summary>
    /// Set environment variables for local development (no Secrets Manager, local broker).
    /// </summary>
    private static void EnsureLocalEnvironment()
    {
        Environment.SetEnvironmentVariable("Lambda__BrokerHost", "localhost");
        Environment.SetEnvironmentVariable("Lambda__BrokerPort", "5672");
        Environment.SetEnvironmentVariable("Lambda__BrokerUseSsl", "false");
        Environment.SetEnvironmentVariable("Lambda__BrokerUsername", "guest");
        Environment.SetEnvironmentVariable("Lambda__BrokerPassword", "guest");
        Environment.SetEnvironmentVariable("Lambda__ResultExchange", "bom.extraction");
        Environment.SetEnvironmentVariable("Lambda__ResultRoutingKey", "extract.result");
        Environment.SetEnvironmentVariable("Lambda__TempDirectory", Path.Combine(Path.GetTempPath(), "bom-extraction"));

        // BomExtraction settings — use your real Bedrock credentials via AWS CLI profile
        Environment.SetEnvironmentVariable("BomExtraction__Region",
            Environment.GetEnvironmentVariable("BomExtraction__Region") ?? "us-east-2");
        Environment.SetEnvironmentVariable("BomExtraction__ModelId",
            Environment.GetEnvironmentVariable("BomExtraction__ModelId") ?? "us.amazon.nova-pro-v1:0");
    }
}

/// <summary>
/// Minimal ILambdaContext implementation for local debugging.
/// </summary>
internal class LocalLambdaContext : ILambdaContext
{
    public string AwsRequestId { get; } = Guid.NewGuid().ToString();
    public IClientContext ClientContext => null!;
    public string FunctionName => "bom-extraction-dotnet-local";
    public string FunctionVersion => "$LATEST";
    public ICognitoIdentity Identity => null!;
    public string InvokedFunctionArn => "arn:aws:lambda:us-east-2:000000000000:function:bom-extraction-dotnet-local";
    public ILambdaLogger Logger { get; } = new ConsoleLambdaLogger();
    public string LogGroupName => "/aws/lambda/bom-extraction-dotnet-local";
    public string LogStreamName => "local";
    public int MemoryLimitInMB => 512;
    public TimeSpan RemainingTime => TimeSpan.FromMinutes(3);
}

internal class ConsoleLambdaLogger : ILambdaLogger
{
    public void Log(string message) => Console.Write(message);
    public void LogLine(string message) => Console.WriteLine(message);
}
