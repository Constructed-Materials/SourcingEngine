using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SourcingEngine.Core.Configuration;
using SourcingEngine.Core.Repositories;
using SourcingEngine.Core.Services;
using SourcingEngine.Data.Repositories;
using SourcingEngine.Data.Services;

namespace SourcingEngine.Data;

/// <summary>
/// Shared DI registration for the SourcingEngine stack.
/// Used by both Program.cs and test fixtures to eliminate duplicate setup.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all SourcingEngine services: configuration, data layer, core services,
    /// and Bedrock embedding/LLM services (required).
    /// </summary>
    public static IServiceCollection AddSourcingEngine(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configuration
        services.Configure<DatabaseSettings>(configuration.GetSection("Database"));
        services.Configure<SemanticSearchSettings>(configuration.GetSection("SemanticSearch"));

        // Data layer
        services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
        services.AddSingleton<ISchemaDiscoveryService, SchemaDiscoveryService>();
        services.AddScoped<IMaterialFamilyRepository, MaterialFamilyRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IProductEnrichedRepository, ProductEnrichedRepository>();
        services.AddScoped<ISemanticProductRepository, SemanticProductRepository>();

        // Core services
        services.AddMemoryCache();
        services.AddSingleton<IProductEmbeddingTextBuilder, ProductEmbeddingTextBuilder>();
        services.AddSingleton<IQueryEmbeddingTextBuilder, QueryEmbeddingTextBuilder>();
        services.AddSingleton<ISpecMatchReRanker, SpecMatchReRanker>();

        // Bedrock embedding & LLM services (required)
        var bedrockSettings = configuration.GetSection("Bedrock").Get<BedrockSettings>();
        if (bedrockSettings?.Enabled != true)
        {
            throw new InvalidOperationException(
                "Bedrock must be enabled. Set Bedrock:Enabled=true in configuration.");
        }

        services.Configure<BedrockSettings>(configuration.GetSection("Bedrock"));
        services.AddSingleton<IEmbeddingService, BedrockEmbeddingService>();
        services.AddSingleton<IQueryParserService, BedrockQueryParserService>();

        // Search strategy — single ProductFirst strategy
        services.AddScoped<ISearchStrategy, ProductFirstStrategy>();
        services.AddScoped<ISearchOrchestrator, SearchOrchestrator>();
        services.AddScoped<IEmbeddingGenerationService, EmbeddingGenerationService>();

        return services;
    }
}
