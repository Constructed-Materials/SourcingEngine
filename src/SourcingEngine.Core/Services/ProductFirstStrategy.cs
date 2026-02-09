using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SourcingEngine.Core.Configuration;
using SourcingEngine.Core.Models;
using SourcingEngine.Core.Repositories;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Product-first search: directly search products by semantic embedding similarity,
/// then enrich matches with vendor-specific data.
/// Bypasses family resolution, using product embeddings instead.
/// </summary>
public class ProductFirstStrategy : ISearchStrategy
{
    private readonly ISemanticProductRepository _semanticProductRepository;
    private readonly IProductEnrichedRepository _productEnrichedRepository;
    private readonly IMaterialFamilyRepository _materialFamilyRepository;
    private readonly IEmbeddingService _embeddingService;
    private readonly IQueryParserService? _queryParserService;
    private readonly SemanticSearchSettings _settings;
    private readonly ILogger<ProductFirstStrategy> _logger;

    public SemanticSearchMode Mode => SemanticSearchMode.ProductFirst;

    public ProductFirstStrategy(
        ISemanticProductRepository semanticProductRepository,
        IProductEnrichedRepository productEnrichedRepository,
        IMaterialFamilyRepository materialFamilyRepository,
        IEmbeddingService embeddingService,
        IOptions<SemanticSearchSettings> settings,
        ILogger<ProductFirstStrategy> logger,
        IQueryParserService? queryParserService = null)
    {
        _semanticProductRepository = semanticProductRepository;
        _productEnrichedRepository = productEnrichedRepository;
        _materialFamilyRepository = materialFamilyRepository;
        _embeddingService = embeddingService;
        _queryParserService = queryParserService;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SearchStrategyResult> ExecuteAsync(
        string bomText, BomItem bomItem, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        try
        {
            // Step 1: LLM query enrichment (optional)
            var textToEmbed = await EnrichQueryAsync(bomText, cancellationToken);

            // Step 2: Generate embedding
            var embedding = await _embeddingService.GenerateEmbeddingAsync(textToEmbed, cancellationToken);

            // Step 3: Semantic search
            var semanticMatches = await _semanticProductRepository.SearchByEmbeddingAsync(
                embedding,
                _settings.SimilarityThreshold,
                _settings.MatchCount,
                cancellationToken);

            _logger.LogInformation("Semantic search found {Count} products above threshold {Threshold}",
                semanticMatches.Count, _settings.SimilarityThreshold);

            // Step 4: Derive family label from most common result
            var familyLabel = semanticMatches
                .Where(sm => !string.IsNullOrWhiteSpace(sm.FamilyLabel))
                .GroupBy(sm => sm.FamilyLabel)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key;

            // Step 5: Derive CSI code
            var csiCode = semanticMatches
                .FirstOrDefault(sm => !string.IsNullOrWhiteSpace(sm.CsiCode))?.CsiCode;

            // Step 6: Enrich with vendor-specific data
            var productIds = semanticMatches.Select(sm => sm.ProductId).ToList();
            var enrichedData = await _productEnrichedRepository.GetEnrichedDataAsync(productIds, cancellationToken);
            var enrichedLookup = enrichedData.ToDictionary(e => e.ProductId, e => e);

            _logger.LogDebug("Enriched {EnrichedCount}/{TotalCount} semantic matches with vendor data",
                enrichedData.Count, semanticMatches.Count);

            // Step 7: Convert to ProductMatch
            var matches = semanticMatches.Select(sm =>
            {
                enrichedLookup.TryGetValue(sm.ProductId, out var enriched);
                return new ProductMatch
                {
                    ProductId = sm.ProductId,
                    Vendor = sm.VendorName,
                    ModelName = sm.ModelName,
                    ModelCode = enriched?.ModelCode,
                    CsiCode = sm.CsiCode,
                    UseWhen = enriched?.UseWhen,
                    KeyFeatures = QueryUtilities.ParseJsonArray(enriched?.KeyFeaturesJson),
                    TechnicalSpecs = QueryUtilities.ParseJsonObject(enriched?.TechnicalSpecsJson),
                    PerformanceData = QueryUtilities.ParseJsonObject(enriched?.PerformanceDataJson),
                    SourceSchema = enriched?.SourceSchema,
                    SemanticScore = sm.Similarity
                };
            }).ToList();

            return new SearchStrategyResult
            {
                Matches = matches,
                Warnings = warnings,
                FamilyLabel = familyLabel,
                CsiCode = csiCode
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Semantic product search failed");
            warnings.Add($"Semantic search failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Use the LLM query parser (if available) to enrich the raw BOM text
    /// before generating an embedding.
    /// </summary>
    private async Task<string> EnrichQueryAsync(string bomText, CancellationToken cancellationToken)
    {
        if (_queryParserService == null)
            return bomText;

        try
        {
            var parsed = await _queryParserService.ParseAsync(bomText, cancellationToken);
            if (parsed.Success && !string.IsNullOrWhiteSpace(parsed.SearchQuery))
            {
                _logger.LogInformation(
                    "LLM parsed '{OriginalText}' → '{EnrichedQuery}' (confidence: {Confidence:F2})",
                    bomText, parsed.SearchQuery, parsed.Confidence);
                return parsed.SearchQuery;
            }

            _logger.LogDebug("Query parser returned no enriched query, using raw text");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Query parsing failed, falling back to raw BOM text");
        }

        return bomText;
    }
}
