using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SourcingEngine.Core.Configuration;
using SourcingEngine.Core.Repositories;
using SourcingEngine.Core.Services;
using SourcingEngine.Data;
using SourcingEngine.Data.Repositories;
using Xunit;

namespace SourcingEngine.Tests.Fixtures;

/// <summary>
/// Shared database fixture for integration tests.
/// Automatically detects Ollama availability and registers the appropriate
/// embedding service (768d Ollama nomic-embed-text or 384d local bge-micro-v2).
/// </summary>
public class DatabaseFixture : IAsyncLifetime
{
    public IServiceProvider ServiceProvider { get; private set; } = null!;
    public IDbConnectionFactory ConnectionFactory => ServiceProvider.GetRequiredService<IDbConnectionFactory>();
    
    /// <summary>
    /// Whether Ollama was detected as running and available during fixture setup.
    /// Tests requiring Ollama-based embeddings can use this to skip gracefully.
    /// </summary>
    public bool IsOllamaAvailable { get; private set; }

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Test.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Register all SourcingEngine services via shared extension
        services.AddSourcingEngine(configuration);

        // Check Ollama availability for test skip support
        var ollamaSettings = configuration.GetSection("Ollama").Get<OllamaSettings>();
        if (ollamaSettings?.Enabled == true)
        {
            IsOllamaAvailable = await OllamaHealthCheck.IsAvailableAsync(ollamaSettings.BaseUrl);
        }

        ServiceProvider = services.BuildServiceProvider();

        // Verify database connection on startup
        await using var connection = await ConnectionFactory.CreateConnectionAsync();
        // Connection successful
    }

    public Task DisposeAsync()
    {
        if (ServiceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
        return Task.CompletedTask;
    }

    public T GetService<T>() where T : notnull => ServiceProvider.GetRequiredService<T>();
    public IServiceScope CreateScope() => ServiceProvider.CreateScope();
}

/// <summary>
/// Collection definition for sharing the database fixture
/// </summary>
[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
}
