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
        var embeddingService = host.Services.GetRequiredService<IEmbeddingService>();
        var textBuilder = host.Services.GetRequiredService<IProductEmbeddingTextBuilder>();

        // Parse options
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
        if (missingOnly) System.Console.WriteLine("Mode: Missing embeddings only");
        if (generateAll) System.Console.WriteLine("Mode: All products");
        if (specificProductId.HasValue) System.Console.WriteLine($"Mode: Product ID {specificProductId}");
        if (familyLabel != null) System.Console.WriteLine($"Mode: Family label '{familyLabel}'");

        try
        {
            using var scope = host.Services.CreateScope();
            var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();

            // Build SQL query based on options
            var whereConditions = new List<string> { "p.is_active = true" };
            
            if (missingOnly)
                whereConditions.Add("p.embedding IS NULL");
            if (specificProductId.HasValue)
                whereConditions.Add($"p.product_id = '{specificProductId}'");
            if (familyLabel != null)
                whereConditions.Add($"p.family_label = '{familyLabel}'");

            var sql = $@"
                SELECT p.product_id, p.model_name, p.family_label, v.name AS vendor_name,
                       pk.description, pk.use_cases::text, pk.specifications::text,
                       pk.ideal_applications::text, pk.not_recommended_for::text
                FROM public.products p
                JOIN public.vendors v ON p.vendor_id = v.vendor_id
                LEFT JOIN public.product_knowledge pk ON p.product_id = pk.product_id
                WHERE {string.Join(" AND ", whereConditions)}
                ORDER BY p.product_id";

            await using var connection = await connectionFactory.CreateConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            var products = new List<ProductEmbeddingInput>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                products.Add(new ProductEmbeddingInput
                {
                    ProductId = reader.GetGuid(0),
                    ModelName = reader.IsDBNull(1) ? null : reader.GetString(1),
                    FamilyLabel = reader.IsDBNull(2) ? null : reader.GetString(2),
                    VendorName = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                    UseCases = reader.IsDBNull(5) ? null : reader.GetString(5),
                    SpecificationsJson = reader.IsDBNull(6) ? null : reader.GetString(6),
                    IdealApplications = reader.IsDBNull(7) ? null : reader.GetString(7),
                    NotRecommendedFor = reader.IsDBNull(8) ? null : reader.GetString(8)
                });
            }

            System.Console.WriteLine($"Found {products.Count} products to process");

            if (products.Count == 0)
            {
                System.Console.WriteLine("No products to process.");
                return 0;
            }

            int processed = 0;
            int failed = 0;

            foreach (var product in products)
            {
                try
                {
                    // Build embedding text
                    var embeddingText = textBuilder.BuildEmbeddingText(product);
                    
                    if (string.IsNullOrWhiteSpace(embeddingText))
                    {
                        logger.LogWarning("Skipping product {ProductId} - empty embedding text", product.ProductId);
                        continue;
                    }

                    // Generate embedding
                    var embedding = await embeddingService.GenerateEmbeddingAsync(embeddingText);
                    
                    // Update database
                    await UpdateProductEmbeddingAsync(connectionFactory, product.ProductId, embedding, embeddingText);

                    processed++;
                    if (processed % 10 == 0 || products.Count < 10)
                    {
                        System.Console.WriteLine($"Processed {processed}/{products.Count}...");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to generate embedding for product {ProductId}", product.ProductId);
                    failed++;
                }
            }

            System.Console.WriteLine($"Embedding generation complete: {processed} succeeded, {failed} failed");
            return failed > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Embedding generation failed");
            System.Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    static async Task UpdateProductEmbeddingAsync(
        IDbConnectionFactory connectionFactory, 
        Guid productId, 
        float[] embedding,
        string embeddingText)
    {
        await using var connection = await connectionFactory.CreateConnectionAsync();
        await using var command = connection.CreateCommand();
        
        // Format embedding as PostgreSQL vector literal
        var vectorLiteral = "[" + string.Join(",", embedding) + "]";
        
        command.CommandText = @"
            UPDATE public.products 
            SET embedding = @embedding::vector,
                embedding_text = @embedding_text,
                embedding_updated_at = NOW()
            WHERE product_id = @product_id";

        var embeddingParam = command.CreateParameter();
        embeddingParam.ParameterName = "embedding";
        embeddingParam.Value = vectorLiteral;
        command.Parameters.Add(embeddingParam);

        var textParam = command.CreateParameter();
        textParam.ParameterName = "embedding_text";
        textParam.Value = embeddingText;
        command.Parameters.Add(textParam);

        var idParam = command.CreateParameter();
        idParam.ParameterName = "product_id";
        idParam.Value = productId;
        command.Parameters.Add(idParam);

        await command.ExecuteNonQueryAsync();
    }

    static async Task<int> SeedFamilyEmbeddingsAsync()
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

                // Ollama settings
                services.Configure<OllamaSettings>(
                    context.Configuration.GetSection("Ollama"));

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
                
                // Ollama services - use if enabled
                var ollamaSettings = context.Configuration
                    .GetSection("Ollama")
                    .Get<OllamaSettings>();
                
                if (ollamaSettings?.Enabled == true)
                {
                    services.AddHttpClient<IEmbeddingService, OllamaEmbeddingService>();
                    services.AddHttpClient<IQueryParserService, OllamaQueryParserService>();
                }
                else
                {
                    services.AddSingleton<IEmbeddingService, LocalEmbeddingService>();
                }
                
                services.AddSingleton<ISearchFusionService, RrfFusionService>();
                services.AddScoped<ISearchOrchestrator, SearchOrchestrator>();
            });
}
