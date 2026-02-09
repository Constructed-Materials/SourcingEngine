using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SourcingEngine.Core.Configuration;
using SourcingEngine.Core.Models;
using SourcingEngine.Core.Repositories;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Family-first search: resolve material family via hybrid FTS+semantic,
/// then retrieve products filtered by family/size/keywords.
/// Also handles <see cref="SemanticSearchMode.Off"/> (keyword-only fallback).
/// </summary>
public class FamilyFirstStrategy : ISearchStrategy
{
    private readonly IMaterialFamilyRepository _materialFamilyRepository;
    private readonly IProductRepository _productRepository;
    private readonly IProductEnrichedRepository _productEnrichedRepository;
    private readonly IEmbeddingService _embeddingService;
    private readonly ISearchFusionService _fusionService;
    private readonly SemanticSearchSettings _settings;
    private readonly ILogger<FamilyFirstStrategy> _logger;

    public SemanticSearchMode Mode => SemanticSearchMode.FamilyFirst;

    public FamilyFirstStrategy(
        IMaterialFamilyRepository materialFamilyRepository,
        IProductRepository productRepository,
        IProductEnrichedRepository productEnrichedRepository,
        IEmbeddingService embeddingService,
        ISearchFusionService fusionService,
        IOptions<SemanticSearchSettings> settings,
        ILogger<FamilyFirstStrategy> logger)
    {
        _materialFamilyRepository = materialFamilyRepository;
        _productRepository = productRepository;
        _productEnrichedRepository = productEnrichedRepository;
        _embeddingService = embeddingService;
        _fusionService = fusionService;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SearchStrategyResult> ExecuteAsync(
        string bomText, BomItem bomItem, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        // Find material family using hybrid search
        var families = await FindMaterialFamiliesAsync(bomText, bomItem.Synonyms, cancellationToken);
        var primaryFamily = families.FirstOrDefault();

        if (primaryFamily == null)
        {
            warnings.Add("No material family found for the given keywords");
            _logger.LogWarning("No material family found for: {Keywords}", string.Join(", ", bomItem.Synonyms));
        }
        else
        {
            _logger.LogInformation("Found material family: {FamilyLabel} ({FamilyName})",
                primaryFamily.FamilyLabel, primaryFamily.FamilyName);
        }

        var csiCode = primaryFamily?.CsiDivision != null
            ? $"{primaryFamily.CsiDivision}2200"
            : null;

        // Search products by family
        var products = await _productRepository.FindProductsAsync(
            primaryFamily?.FamilyLabel,
            null,
            bomItem.SizeVariants.Count > 0 ? bomItem.SizeVariants : null,
            bomItem.Synonyms,
            cancellationToken);

        _logger.LogInformation("Found {ProductCount} products matching family criteria", products.Count);

        // Get enriched data
        var productIds = products.Select(p => p.ProductId).ToList();
        var enrichedData = await _productEnrichedRepository.GetEnrichedDataAsync(productIds, cancellationToken);
        var enrichedLookup = enrichedData.ToDictionary(e => e.ProductId, e => e);

        var matches = products.Select(p =>
        {
            enrichedLookup.TryGetValue(p.ProductId, out var enriched);
            return QueryUtilities.CreateProductMatch(p, enriched);
        }).ToList();

        return new SearchStrategyResult
        {
            Matches = matches,
            Warnings = warnings,
            FamilyLabel = primaryFamily?.FamilyLabel,
            CsiCode = products.FirstOrDefault()?.CsiSectionCode ?? csiCode
        };
    }

    /// <summary>
    /// Find material families using hybrid semantic + full-text search with RRF fusion.
    /// Falls back to keyword search if semantic search fails or is disabled.
    /// </summary>
    internal async Task<List<MaterialFamily>> FindMaterialFamiliesAsync(
        string queryText,
        IEnumerable<string> fallbackKeywords,
        CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogDebug("Semantic search disabled, using keyword search");
            return await _materialFamilyRepository.FindByKeywordsAsync(fallbackKeywords, cancellationToken);
        }

        try
        {
            // Generate embedding for the query
            _logger.LogDebug("Generating embedding for query: {Query}", queryText);
            var embedding = await _embeddingService.GenerateEmbeddingAsync(queryText, cancellationToken);

            // Clean query for FTS
            var ftsQuery = QueryUtilities.CleanQueryForFts(queryText);
            _logger.LogDebug("Cleaned FTS query: {FtsQuery}", ftsQuery);

            // Run both searches in parallel
            var ftsTask = _materialFamilyRepository.FullTextSearchAsync(
                ftsQuery,
                _settings.MatchCount * 2,
                cancellationToken);
            var semanticTask = _materialFamilyRepository.SemanticSearchAsync(
                embedding,
                _settings.MatchCount * 2,
                cancellationToken);

            await Task.WhenAll(ftsTask, semanticTask);

            var ftsResults = await ftsTask;
            var semanticResults = await semanticTask;

            _logger.LogDebug("FTS returned {FtsCount}, Semantic returned {SemCount}",
                ftsResults.Count, semanticResults.Count);

            // Fuse results using RRF
            var fusedResults = _fusionService.Fuse(
                ftsResults,
                semanticResults,
                _settings.FullTextWeight,
                _settings.SemanticWeight,
                _settings.RrfK,
                _settings.MatchCount);

            if (fusedResults.Count > 0)
                return fusedResults;

            _logger.LogDebug("Hybrid search returned no results, falling back to keyword search");
            return await _materialFamilyRepository.FindByKeywordsAsync(fallbackKeywords, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Semantic search failed, falling back to keyword search");
            return await _materialFamilyRepository.FindByKeywordsAsync(fallbackKeywords, cancellationToken);
        }
    }
}
