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

        // Configuration
        services.Configure<DatabaseSettings>(configuration.GetSection("Database"));
        services.Configure<SemanticSearchSettings>(configuration.GetSection("SemanticSearch"));
        services.Configure<OllamaSettings>(configuration.GetSection("Ollama"));

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
        services.AddScoped<ISemanticProductRepository, SemanticProductRepository>();

        // Core services
        services.AddMemoryCache();
        services.AddSingleton<ISizeCalculator, SizeCalculator>();
        services.AddSingleton<ISynonymExpander, SynonymExpander>();
        services.AddSingleton<IInputNormalizer, InputNormalizer>();
        services.AddSingleton<IProductEmbeddingTextBuilder, ProductEmbeddingTextBuilder>();
        services.AddSingleton<ISearchFusionService, RrfFusionService>();

        // Check Ollama availability and register appropriate services
        var ollamaSettings = configuration.GetSection("Ollama").Get<OllamaSettings>();
        if (ollamaSettings?.Enabled == true)
        {
            IsOllamaAvailable = await CheckOllamaAvailableAsync(ollamaSettings.BaseUrl);
        }

        if (IsOllamaAvailable)
        {
            // Use Ollama for embeddings (768d nomic-embed-text) and query parsing (llama3.2:3b)
            services.AddHttpClient<IEmbeddingService, OllamaEmbeddingService>();
            services.AddHttpClient<IQueryParserService, OllamaQueryParserService>();
        }
        else
        {
            // Fallback: local embedding service (384d bge-micro-v2), no query parser
            services.AddSingleton<IEmbeddingService, LocalEmbeddingService>();
        }

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

    /// <summary>
    /// Quick health check: can we reach the Ollama API?
    /// </summary>
    private static async Task<bool> CheckOllamaAvailableAsync(string baseUrl)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = await httpClient.GetAsync($"{baseUrl.TrimEnd('/')}/api/tags");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Collection definition for sharing the database fixture
/// </summary>
[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
}
