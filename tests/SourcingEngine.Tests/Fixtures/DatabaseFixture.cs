using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SourcingEngine.Core.Services;
using SourcingEngine.Data;
using Xunit;

namespace SourcingEngine.Tests.Fixtures;

/// <summary>
/// Shared database fixture for integration tests.
/// Registers all SourcingEngine services via the shared DI extension.
/// Requires Bedrock to be enabled in appsettings.Test.json.
/// </summary>
public class DatabaseFixture : IAsyncLifetime
{
    public IServiceProvider ServiceProvider { get; private set; } = null!;
    public IDbConnectionFactory ConnectionFactory => ServiceProvider.GetRequiredService<IDbConnectionFactory>();

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

        ServiceProvider = services.BuildServiceProvider();

        // Verify database connection on startup
        await using var connection = await ConnectionFactory.CreateConnectionAsync();
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
