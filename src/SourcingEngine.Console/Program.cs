using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SourcingEngine.Core.Configuration;
using SourcingEngine.Core.Repositories;
using SourcingEngine.Core.Services;
using SourcingEngine.Data;
using SourcingEngine.Data.Repositories;

namespace SourcingEngine.Console;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Check for seed-embeddings command
        if (args.Length > 0 && args[0] == "--seed-embeddings")
        {
            return await SeedEmbeddingsAsync();
        }

        if (args.Length == 0)
        {
            System.Console.WriteLine("Usage: SourcingEngine.Console <bom-text>");
            System.Console.WriteLine("       SourcingEngine.Console --seed-embeddings");
            System.Console.WriteLine("Example: SourcingEngine.Console \"8 inch masonry block\"");
            return 1;
        }

        var bomText = string.Join(" ", args);

        var host = CreateHostBuilder().Build();
        
        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        var orchestrator = host.Services.GetRequiredService<ISearchOrchestrator>();

        try
        {
            logger.LogInformation("Searching for: {BomText}", bomText);
            
            var result = await orchestrator.SearchAsync(bomText);

            // Output JSON result
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(result, jsonOptions);
            System.Console.WriteLine(json);

            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Search failed");
            System.Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    static async Task<int> SeedEmbeddingsAsync()
    {
        System.Console.WriteLine("Seeding embeddings for all material families...");
        
        var host = CreateHostBuilder().Build();
        
        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        var embeddingService = host.Services.GetRequiredService<IEmbeddingService>();
        
        using var scope = host.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMaterialFamilyRepository>();

        try
        {
            var families = await repository.GetAllAsync();
            System.Console.WriteLine($"Found {families.Count} material families to process");

            int processed = 0;
            int failed = 0;

            foreach (var family in families)
            {
                try
                {
                    // Build text for embedding: combine label, name, and synonyms
                    var textToEmbed = string.Join(" ", new[]
                    {
                        family.FamilyLabel?.Replace("_", " "),
                        family.FamilyName,
                        family.Synonyms
                    }.Where(s => !string.IsNullOrWhiteSpace(s)));

                    if (string.IsNullOrWhiteSpace(textToEmbed))
                    {
                        logger.LogWarning("Skipping {FamilyLabel} - no text to embed", family.FamilyLabel);
                        continue;
                    }

                    var embedding = await embeddingService.GenerateEmbeddingAsync(textToEmbed);
                    await repository.UpdateEmbeddingAsync(family.FamilyLabel!, embedding);
                    
                    processed++;
                    if (processed % 10 == 0)
                    {
                        System.Console.WriteLine($"Processed {processed}/{families.Count}...");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to generate embedding for {FamilyLabel}", family.FamilyLabel);
                    failed++;
                }
            }

            System.Console.WriteLine($"Seeding complete: {processed} succeeded, {failed} failed");
            return failed > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Embedding seeding failed");
            System.Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    static IHostBuilder CreateHostBuilder() =>
        Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((context, config) =>
            {
                // Get the directory where the executable is located
                var basePath = AppContext.BaseDirectory;
                config.SetBasePath(basePath)
                      .AddJsonFile("appsettings.json", optional: false)
                      .AddEnvironmentVariables();
            })
            .ConfigureLogging((context, logging) =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.AddConfiguration(context.Configuration.GetSection("Logging"));
            })
            .ConfigureServices((context, services) =>
            {
                // Database settings
                services.Configure<DatabaseSettings>(
                    context.Configuration.GetSection("Database"));

                // Semantic search settings
                services.Configure<SemanticSearchSettings>(
                    context.Configuration.GetSection("SemanticSearch"));

                // Data layer
                services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
                services.AddSingleton<ISchemaDiscoveryService, SchemaDiscoveryService>();
                services.AddScoped<IMaterialFamilyRepository, MaterialFamilyRepository>();
                services.AddScoped<IProductRepository, ProductRepository>();
                services.AddScoped<IProductEnrichedRepository, ProductEnrichedRepository>();

                // Core services
                services.AddMemoryCache();
                services.AddSingleton<ISizeCalculator, SizeCalculator>();
                services.AddSingleton<ISynonymExpander, SynonymExpander>();
                services.AddSingleton<IInputNormalizer, InputNormalizer>();
                services.AddSingleton<IEmbeddingService, LocalEmbeddingService>();
                services.AddSingleton<ISearchFusionService, RrfFusionService>();
                services.AddScoped<ISearchOrchestrator, SearchOrchestrator>();
            });
}
