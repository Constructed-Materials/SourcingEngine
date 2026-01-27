using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SourcingEngine.Core.Repositories;
using SourcingEngine.Core.Services;
using SourcingEngine.Data;
using SourcingEngine.Data.Repositories;

namespace SourcingEngine.Console;

class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            System.Console.WriteLine("Usage: SourcingEngine.Console <bom-text>");
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

    static IHostBuilder CreateHostBuilder() =>
        Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: false)
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
            });
}
