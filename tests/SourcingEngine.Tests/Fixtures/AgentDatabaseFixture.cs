using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SourcingEngine.Core.Services;
using SourcingEngine.Data;
using Xunit;

namespace SourcingEngine.Tests.Fixtures;

/// <summary>
/// Shared fixture for agent-based integration tests.
/// Loads appsettings.AgentTest.json with Agent.Enabled=true,
/// so <see cref="AgentSearchStrategy"/> is registered as <see cref="ISearchStrategy"/>.
/// </summary>
public class AgentDatabaseFixture : IAsyncLifetime
{
    public IServiceProvider ServiceProvider { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.AgentTest.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Register all SourcingEngine services — with Agent.Enabled=true
        // this will wire AgentSearchStrategy instead of ProductFirstStrategy
        services.AddSourcingEngine(configuration);

        ServiceProvider = services.BuildServiceProvider();

        await Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (ServiceProvider is IDisposable disposable)
            disposable.Dispose();
        return Task.CompletedTask;
    }

    public T GetService<T>() where T : notnull => ServiceProvider.GetRequiredService<T>();
    public IServiceScope CreateScope() => ServiceProvider.CreateScope();
}

/// <summary>
/// Collection definition for sharing the agent database fixture.
/// </summary>
[CollectionDefinition("AgentDatabase")]
public class AgentDatabaseCollection : ICollectionFixture<AgentDatabaseFixture>
{
}
