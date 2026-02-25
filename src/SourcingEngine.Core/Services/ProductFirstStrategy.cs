using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SourcingEngine.Common.Models;
using SourcingEngine.Core.Configuration;
using SourcingEngine.Core.Models;
using SourcingEngine.Core.Repositories;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Product-first search: parse the BOM item via LLM, build a structured query
/// embedding aligned with the product embedding format, and search products
/// directly by semantic similarity.
/// </summary>
public class ProductFirstStrategy : ISearchStrategy
{
    private readonly ISemanticProductRepository _semanticProductRepository;
    private readonly IProductEnrichedRepository _productEnrichedRepository;
    private readonly IEmbeddingService _embeddingService;
    private readonly IQueryParserService _queryParserService;
    private readonly IQueryEmbeddingTextBuilder _queryEmbeddingTextBuilder;
    private readonly SemanticSearchSettings _settings;
    private readonly ILogger<ProductFirstStrategy> _logger;

    public ProductFirstStrategy(
        ISemanticProductRepository semanticProductRepository,
        IProductEnrichedRepository productEnrichedRepository,
        IEmbeddingService embeddingService,
        IQueryParserService queryParserService,
        IQueryEmbeddingTextBuilder queryEmbeddingTextBuilder,
        IOptions<SemanticSearchSettings> settings,
        ILogger<ProductFirstStrategy> logger)
    {
        _semanticProductRepository = semanticProductRepository;
        _productEnrichedRepository = productEnrichedRepository;
        _embeddingService = embeddingService;
        _queryParserService = queryParserService;
        _queryEmbeddingTextBuilder = queryEmbeddingTextBuilder;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SearchStrategyResult> ExecuteAsync(
        BomLineItem item, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        try
        {
            // Step 1: LLM query parsing (mandatory) — extract family, dimensions, attributes
            var searchText = item.Spec;
            if (string.IsNullOrWhiteSpace(searchText))
                searchText = item.BomItem;

            var parsedQuery = await ParseQueryAsync(searchText, cancellationToken);
            if (!parsedQuery.Success)
            {
                warnings.Add($"LLM parsing failed: {parsedQuery.ErrorMessage}. Using raw spec text.");
            }

            // Step 2: Build structured embedding text aligned with product format
            var textToEmbed = parsedQuery.Success
                ? _queryEmbeddingTextBuilder.BuildQueryEmbeddingText(item, parsedQuery)
                : searchText;

            _logger.LogInformation(
                "Embedding text for '{BomItem}': {EmbeddingText}",
                item.BomItem, EmbeddingUtilities.TruncateForLog(textToEmbed));

            // Step 3: Generate embedding
            var embedding = await _embeddingService.GenerateEmbeddingAsync(textToEmbed, cancellationToken);

            // Step 4: Semantic search
            var semanticMatches = await _semanticProductRepository.SearchByEmbeddingAsync(
                embedding,
                _settings.SimilarityThreshold,
                _settings.MatchCount,
                cancellationToken);

            _logger.LogInformation("Semantic search found {Count} products above threshold {Threshold}",
                semanticMatches.Count, _settings.SimilarityThreshold);

            // Step 5: Derive family label from most common result
            var familyLabel = semanticMatches
                .Where(sm => !string.IsNullOrWhiteSpace(sm.FamilyLabel))
                .GroupBy(sm => sm.FamilyLabel)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key;

            // Step 6: Derive CSI code
            var csiCode = semanticMatches
                .FirstOrDefault(sm => !string.IsNullOrWhiteSpace(sm.CsiCode))?.CsiCode;

            // Step 7: Enrich with vendor-specific data
            var productIds = semanticMatches.Select(sm => sm.ProductId).ToList();
            var enrichedData = await _productEnrichedRepository.GetEnrichedDataAsync(productIds, cancellationToken);
            var enrichedLookup = enrichedData.ToDictionary(e => e.ProductId, e => e);

            _logger.LogDebug("Enriched {EnrichedCount}/{TotalCount} semantic matches with vendor data",
                enrichedData.Count, semanticMatches.Count);

            // Step 8: Convert to ProductMatch
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
            _logger.LogWarning(ex, "Semantic product search failed for '{BomItem}'", item.BomItem);
            warnings.Add($"Semantic search failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Parse the BOM text via LLM to extract structured material information.
    /// Falls back gracefully on failure.
    /// </summary>
    private async Task<ParsedBomQuery> ParseQueryAsync(string bomText, CancellationToken cancellationToken)
    {
        try
        {
            var parsed = await _queryParserService.ParseAsync(bomText, cancellationToken);
            if (parsed.Success)
            {
                _logger.LogInformation(
                    "LLM parsed '{OriginalText}' → family='{Family}', specs='{Specs}' (confidence: {Confidence:F2})",
                    bomText, parsed.MaterialFamily, parsed.TechnicalSpecs.ToEmbeddingFormat(), parsed.Confidence);
    
            }
            return parsed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Query parsing failed for '{BomText}'", bomText);
            return new ParsedBomQuery
            {
                OriginalInput = bomText,
                SearchQuery = bomText,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
