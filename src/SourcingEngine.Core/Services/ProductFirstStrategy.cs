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
    private readonly IEmbeddingService _embeddingService;
    private readonly IQueryParserService _queryParserService;
    private readonly IQueryEmbeddingTextBuilder _queryEmbeddingTextBuilder;
    private readonly ISpecMatchReRanker _specMatchReRanker;
    private readonly SemanticSearchSettings _settings;
    private readonly ILogger<ProductFirstStrategy> _logger;

    public ProductFirstStrategy(
        ISemanticProductRepository semanticProductRepository,
        IEmbeddingService embeddingService,
        IQueryParserService queryParserService,
        IQueryEmbeddingTextBuilder queryEmbeddingTextBuilder,
        ISpecMatchReRanker specMatchReRanker,
        IOptions<SemanticSearchSettings> settings,
        ILogger<ProductFirstStrategy> logger)
    {
        _semanticProductRepository = semanticProductRepository;
        _embeddingService = embeddingService;
        _queryParserService = queryParserService;
        _queryEmbeddingTextBuilder = queryEmbeddingTextBuilder;
        _specMatchReRanker = specMatchReRanker;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SearchStrategyResult> ExecuteAsync(
        BomLineItem item, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        try
        {
            _logger.LogInformation(
                "Semantic search settings - Threshold: {Threshold}, MatchCount: {MatchCount}, SimilarityThreshold: {SimilarityThreshold}",
                _settings.SimilarityThreshold, _settings.MatchCount, _settings.SimilarityThreshold);
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

            // Step 4: Build inline search filters from parsed query (hybrid search)
            var filters = parsedQuery.Success
                ? BuildSearchFilters(parsedQuery)
                : null;

            // Step 5: Semantic search with inline filters
            var semanticMatches = await _semanticProductRepository.SearchByEmbeddingAsync(
                embedding,
                filters,
                _settings.SimilarityThreshold,
                _settings.MatchCount,
                cancellationToken);

            _logger.LogInformation("Semantic search found {Count} products above threshold {Threshold}",
                semanticMatches.Count, _settings.SimilarityThreshold);

            // Step 6: Post-retrieval specification re-ranking
            semanticMatches = _specMatchReRanker.ReRank(semanticMatches,
                parsedQuery.Success ? parsedQuery.TechnicalSpecs : null);

            // Step 6b: Filter out results whose FinalScore dropped below threshold after re-ranking
            var preFilterCount = semanticMatches.Count;
            semanticMatches = semanticMatches
                .Where(sm => (sm.FinalScore ?? sm.Similarity) >= _settings.SimilarityThreshold)
                .ToList();

            if (semanticMatches.Count < preFilterCount)
            {
                _logger.LogInformation(
                    "Post-rerank threshold filter removed {Removed} results (threshold={Threshold})",
                    preFilterCount - semanticMatches.Count, _settings.SimilarityThreshold);
            }

            // Step 7: Derive family label from most common result
            var familyLabel = semanticMatches
                .Where(sm => !string.IsNullOrWhiteSpace(sm.FamilyLabel))
                .GroupBy(sm => sm.FamilyLabel)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key;

            // Step 8: Derive CSI code
            var csiCode = semanticMatches
                .FirstOrDefault(sm => !string.IsNullOrWhiteSpace(sm.CsiCode))?.CsiCode;

            // Step 9: Convert to ProductMatch using public.product_knowledge data
            // (description, use_cases, specifications already returned by semantic search)
            var matches = semanticMatches.Select(sm => new ProductMatch
            {
                ProductId = sm.ProductId,
                Vendor = sm.VendorName,
                ModelName = sm.ModelName,
                CsiCode = sm.CsiCode,
                Description = sm.Description,
                UseCases = QueryUtilities.ParseJsonArray(sm.UseCases),
                TechnicalSpecs = QueryUtilities.ParseJsonObject(sm.SpecificationsJson),
                SemanticScore = sm.Similarity,
                FinalScore = sm.FinalScore
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
    /// Build inline search filters from the LLM-parsed query.
    /// Currently filters by family label when the LLM is confident about it.
    /// </summary>
    private static Models.SearchFilters? BuildSearchFilters(ParsedBomQuery parsedQuery)
    {
        if (string.IsNullOrWhiteSpace(parsedQuery.MaterialFamily))
            return null;

        // Only apply family filter when LLM confidence is high enough to avoid
        // false negatives from incorrect family classification
        if (parsedQuery.Confidence < 0.8f)
            return null;

        return new Models.SearchFilters
        {
            FamilyLabel = parsedQuery.MaterialFamily switch
            {
                // Map LLM material family names to DB family_label values
                "cmu" => "cmu_blocks",
                _ => null // Don't filter on families we can't confidently map
            }
        };
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
