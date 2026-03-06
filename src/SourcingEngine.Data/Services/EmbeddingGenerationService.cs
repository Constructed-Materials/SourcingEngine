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
            whereConditions.Add("p.embedding_description IS NULL");
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
                   pk.ideal_applications::text, pk.not_recommended_for::text,
                   COALESCE(
                       (SELECT string_agg(c.title, ', ' ORDER BY c.title)
                        FROM public.product_certifications pc2
                        JOIN public.certifications c ON pc2.cert_id = c.cert_id
                        WHERE pc2.product_id = p.product_id), '') AS cert_names
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
            var certNamesRaw = reader.IsDBNull(9) ? "" : reader.GetString(9);
            var certList = string.IsNullOrWhiteSpace(certNamesRaw)
                ? new List<string>()
                : certNamesRaw.Split(", ", StringSplitOptions.RemoveEmptyEntries).ToList();

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
                NotRecommendedFor = reader.IsDBNull(8) ? null : reader.GetString(8),
                Certifications = certList
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
                var multiText = await _textBuilder.BuildMultiVectorTextAsync(product, cancellationToken);

                if (string.IsNullOrWhiteSpace(multiText.DescriptionText))
                {
                    _logger.LogWarning("Skipping product {ProductId} - empty description text", product.ProductId);
                    continue;
                }

                var textsToEmbed = new[] { multiText.DescriptionText, multiText.SpecsText, multiText.EnrichmentText };
                var embeddings = await _embeddingService.GenerateEmbeddingsAsync(textsToEmbed, cancellationToken);

                await UpdateProductEmbeddingAsync(
                    product.ProductId,
                    embeddings[0], embeddings[1], embeddings[2],
                    multiText.DescriptionText, multiText.SpecsText, multiText.EnrichmentText,
                    cancellationToken);

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
        float[] descriptionEmbedding,
        float[] specsEmbedding,
        float[] enrichmentEmbedding,
        string descriptionText,
        string specsText,
        string enrichmentText,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            UPDATE public.products 
            SET embedding_description = @emb_desc::vector,
                embedding_specs = @emb_specs::vector,
                embedding_enrichment = @emb_enrich::vector,
                embedding_text_description = @text_desc,
                embedding_text_specs = @text_specs,
                embedding_text_enrichment = @text_enrich,
                embedding_updated_at = NOW()
            WHERE product_id = @product_id";

        void AddParam(string name, object value)
        {
            var p = command.CreateParameter();
            p.ParameterName = name;
            p.Value = value;
            command.Parameters.Add(p);
        }

        AddParam("emb_desc", EmbeddingUtilities.FormatPgVector(descriptionEmbedding));
        AddParam("emb_specs", EmbeddingUtilities.FormatPgVector(specsEmbedding));
        AddParam("emb_enrich", EmbeddingUtilities.FormatPgVector(enrichmentEmbedding));
        AddParam("text_desc", descriptionText);
        AddParam("text_specs", specsText);
        AddParam("text_enrich", enrichmentText);
        AddParam("product_id", productId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
