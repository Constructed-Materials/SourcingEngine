using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SourcingEngine.Core.Services;
using SourcingEngine.Data;

namespace SourcingEngine.Console;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Check for CLI commands
        if (args.Length > 0)
        {
            switch (args[0])
            {
                case "--seed-embeddings":
                    return await SeedFamilyEmbeddingsAsync();
                
                case "--generate-embeddings":
                    return await GenerateProductEmbeddingsAsync(args.Skip(1).ToArray());
            }
        }

        if (args.Length == 0)
        {
            System.Console.WriteLine("Usage:");
            System.Console.WriteLine("  SourcingEngine.Console <bom-text>              - Search for products");
            System.Console.WriteLine("  SourcingEngine.Console --seed-embeddings       - Seed material family embeddings");
            System.Console.WriteLine("  SourcingEngine.Console --generate-embeddings   - Generate product embeddings");
            System.Console.WriteLine("");
            System.Console.WriteLine("Generate embeddings options:");
            System.Console.WriteLine("  --all                     Generate for all products");
            System.Console.WriteLine("  --product-id <uuid>       Generate for specific product UUID");
            System.Console.WriteLine("  --type <family_label>     Generate for products of specific family");
            System.Console.WriteLine("  --missing                 Generate only for products without embeddings");
            System.Console.WriteLine("");
            System.Console.WriteLine("Examples:");
            System.Console.WriteLine("  SourcingEngine.Console \"8 inch masonry block\"");
            System.Console.WriteLine("  SourcingEngine.Console --generate-embeddings --all");
            System.Console.WriteLine("  SourcingEngine.Console --generate-embeddings --missing");
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

    static async Task<int> GenerateProductEmbeddingsAsync(string[] options)
    {
        var host = CreateHostBuilder().Build();
        var logger = host.Services.GetRequiredService<ILogger<Program>>();

        // Parse CLI options
        bool generateAll = options.Contains("--all");
        bool missingOnly = options.Contains("--missing");
        Guid? specificProductId = null;
        string? familyLabel = null;

        for (int i = 0; i < options.Length; i++)
        {
            if (options[i] == "--product-id" && i + 1 < options.Length)
            {
                if (Guid.TryParse(options[i + 1], out var id))
                    specificProductId = id;
            }
            else if (options[i] == "--type" && i + 1 < options.Length)
            {
                familyLabel = options[i + 1];
            }
        }

        if (!generateAll && !missingOnly && specificProductId == null && familyLabel == null)
        {
            System.Console.WriteLine("Please specify one of: --all, --missing, --product-id <uuid>, or --type <family_label>");
            return 1;
        }

        System.Console.WriteLine("Generating product embeddings...");

        try
        {
            using var scope = host.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IEmbeddingGenerationService>();

            var result = await service.GenerateProductEmbeddingsAsync(new EmbeddingGenerationOptions
            {
                GenerateAll = generateAll,
                MissingOnly = missingOnly,
                SpecificProductId = specificProductId,
                FamilyLabel = familyLabel
            });

            System.Console.WriteLine($"Embedding generation complete: {result.Processed} succeeded, {result.Failed} failed");
            return result.Failed > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Embedding generation failed");
            System.Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    static async Task<int> SeedFamilyEmbeddingsAsync()
    {
        System.Console.WriteLine("Seeding embeddings for all material families...");

        var host = CreateHostBuilder().Build();
        var logger = host.Services.GetRequiredService<ILogger<Program>>();

        try
        {
            using var scope = host.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IEmbeddingGenerationService>();

            var result = await service.SeedFamilyEmbeddingsAsync();

            System.Console.WriteLine($"Seeding complete: {result.Processed} succeeded, {result.Failed} failed");
            return result.Failed > 0 ? 1 : 0;
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
                services.AddSourcingEngine(context.Configuration);
            });
}
