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
    /// and conditionally Ollama or local embedding services.
    /// </summary>
    public static IServiceCollection AddSourcingEngine(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configuration
        services.Configure<DatabaseSettings>(configuration.GetSection("Database"));
        services.Configure<SemanticSearchSettings>(configuration.GetSection("SemanticSearch"));
        services.Configure<OllamaSettings>(configuration.GetSection("Ollama"));

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

        // Embedding & LLM services — conditional on Ollama config
        var ollamaSettings = configuration.GetSection("Ollama").Get<OllamaSettings>();
        if (ollamaSettings?.Enabled == true)
        {
            services.AddHttpClient<IEmbeddingService, OllamaEmbeddingService>();
            services.AddHttpClient<IQueryParserService, OllamaQueryParserService>();
        }
        else
        {
            services.AddSingleton<IEmbeddingService, LocalEmbeddingService>();
        }

        // Search strategies (registered as ISearchStrategy for collection injection)
        services.AddScoped<FamilyFirstStrategy>();
        services.AddScoped<ISearchStrategy>(sp => sp.GetRequiredService<FamilyFirstStrategy>());
        // Also register FamilyFirst for SemanticSearchMode.Off (same logic, just with semantic disabled)
        services.AddScoped<ProductFirstStrategy>();
        services.AddScoped<ISearchStrategy>(sp => sp.GetRequiredService<ProductFirstStrategy>());
        services.AddScoped<HybridStrategy>();
        services.AddScoped<ISearchStrategy>(sp => sp.GetRequiredService<HybridStrategy>());

        services.AddScoped<ISearchOrchestrator, SearchOrchestrator>();
        services.AddScoped<IEmbeddingGenerationService, EmbeddingGenerationService>();

        return services;
    }
}
