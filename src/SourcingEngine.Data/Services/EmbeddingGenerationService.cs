using Microsoft.Extensions.Logging;
using SourcingEngine.Core.Models;
using SourcingEngine.Core.Repositories;
using SourcingEngine.Core.Services;

namespace SourcingEngine.Data.Services;

/// <summary>
/// Generates and stores embeddings for products and material families.
/// Uses IDbConnectionFactory for direct SQL operations (embedding updates)
/// and delegates to IEmbeddingService for vector generation.
/// </summary>
public class EmbeddingGenerationService : IEmbeddingGenerationService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IEmbeddingService _embeddingService;
    private readonly IProductEmbeddingTextBuilder _textBuilder;
    private readonly IMaterialFamilyRepository _materialFamilyRepository;
    private readonly ILogger<EmbeddingGenerationService> _logger;

    public EmbeddingGenerationService(
        IDbConnectionFactory connectionFactory,
        IEmbeddingService embeddingService,
        IProductEmbeddingTextBuilder textBuilder,
        IMaterialFamilyRepository materialFamilyRepository,
        ILogger<EmbeddingGenerationService> logger)
    {
        _connectionFactory = connectionFactory;
        _embeddingService = embeddingService;
        _textBuilder = textBuilder;
        _materialFamilyRepository = materialFamilyRepository;
        _logger = logger;
    }

    public async Task<EmbeddingGenerationResult> GenerateProductEmbeddingsAsync(
        EmbeddingGenerationOptions options,
        CancellationToken cancellationToken = default)
    {
        // Build SQL query with parameterized conditions
        var whereConditions = new List<string> { "p.is_active = true" };
        var parameters = new Dictionary<string, object>();

        if (options.MissingOnly)
            whereConditions.Add("p.embedding IS NULL");
        if (options.SpecificProductId.HasValue)
        {
            whereConditions.Add("p.product_id = @specificProductId");
            parameters["specificProductId"] = options.SpecificProductId.Value;
        }
        if (options.FamilyLabel != null)
        {
            whereConditions.Add("p.family_label = @familyLabel");
            parameters["familyLabel"] = options.FamilyLabel;
        }

        var sql = $@"
            SELECT p.product_id, p.model_name, p.family_label, v.name AS vendor_name,
                   pk.description, pk.use_cases::text, pk.specifications::text,
                   pk.ideal_applications::text, pk.not_recommended_for::text
            FROM public.products p
            JOIN public.vendors v ON p.vendor_id = v.vendor_id
            LEFT JOIN public.product_knowledge pk ON p.product_id = pk.product_id
            WHERE {string.Join(" AND ", whereConditions)}
            ORDER BY p.product_id";

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (paramName, paramValue) in parameters)
        {
            var param = command.CreateParameter();
            param.ParameterName = paramName;
            param.Value = paramValue;
            command.Parameters.Add(param);
        }

        var products = new List<ProductEmbeddingInput>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
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

        _logger.LogInformation("Found {Count} products to process", products.Count);

        if (products.Count == 0)
        {
            return new EmbeddingGenerationResult(0, 0, 0);
        }

        int processed = 0;
        int failed = 0;

        foreach (var product in products)
        {
            try
            {
                var embeddingText = _textBuilder.BuildEmbeddingText(product);

                if (string.IsNullOrWhiteSpace(embeddingText))
                {
                    _logger.LogWarning("Skipping product {ProductId} - empty embedding text", product.ProductId);
                    continue;
                }

                var embedding = await _embeddingService.GenerateEmbeddingAsync(embeddingText, cancellationToken);
                await UpdateProductEmbeddingAsync(product.ProductId, embedding, embeddingText, cancellationToken);

                processed++;
                if (processed % 10 == 0 || products.Count < 10)
                {
                    _logger.LogInformation("Processed {Processed}/{Total}...", processed, products.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate embedding for product {ProductId}", product.ProductId);
                failed++;
            }
        }

        return new EmbeddingGenerationResult(processed, failed, products.Count);
    }

    public async Task<EmbeddingGenerationResult> SeedFamilyEmbeddingsAsync(
        CancellationToken cancellationToken = default)
    {
        var families = await _materialFamilyRepository.GetAllAsync(cancellationToken);
        _logger.LogInformation("Found {Count} material families to process", families.Count);

        int processed = 0;
        int failed = 0;

        foreach (var family in families)
        {
            try
            {
                var textToEmbed = string.Join(" ", new[]
                {
                    family.FamilyLabel?.Replace("_", " "),
                    family.FamilyName,
                    family.Synonyms
                }.Where(s => !string.IsNullOrWhiteSpace(s)));

                if (string.IsNullOrWhiteSpace(textToEmbed))
                {
                    _logger.LogWarning("Skipping {FamilyLabel} - no text to embed", family.FamilyLabel);
                    continue;
                }

                var embedding = await _embeddingService.GenerateEmbeddingAsync(textToEmbed, cancellationToken);
                await _materialFamilyRepository.UpdateEmbeddingAsync(family.FamilyLabel!, embedding, cancellationToken);

                processed++;
                if (processed % 10 == 0)
                {
                    _logger.LogInformation("Processed {Processed}/{Total}...", processed, families.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate embedding for {FamilyLabel}", family.FamilyLabel);
                failed++;
            }
        }

        return new EmbeddingGenerationResult(processed, failed, families.Count);
    }

    private async Task UpdateProductEmbeddingAsync(
        Guid productId,
        float[] embedding,
        string embeddingText,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var vectorLiteral = EmbeddingUtilities.FormatPgVector(embedding);

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

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
