using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SourcingEngine.Core.Repositories;
using SourcingEngine.Core.Services;
using SourcingEngine.Data;
using SourcingEngine.Data.Repositories;
using Xunit;

namespace SourcingEngine.Tests.Fixtures;

/// <summary>
/// Shared database fixture for integration tests
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

        // Configuration
        services.Configure<DatabaseSettings>(configuration.GetSection("Database"));

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Data layer
        services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
        services.AddSingleton<ISchemaDiscoveryService, SchemaDiscoveryService>();
        services.AddScoped<IMaterialFamilyRepository, MaterialFamilyRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IProductEnrichedRepository, ProductEnrichedRepository>();

        // Core services
        services.AddSingleton<ISizeCalculator, SizeCalculator>();
        services.AddSingleton<ISynonymExpander, SynonymExpander>();
        services.AddSingleton<IInputNormalizer, InputNormalizer>();
        services.AddScoped<ISearchOrchestrator, SearchOrchestrator>();

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
