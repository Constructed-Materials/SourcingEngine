using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SourcingEngine.Core.Configuration;
using SourcingEngine.Core.Models;
using SourcingEngine.Core.Repositories;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Main search orchestrator implementation with hybrid semantic search support
/// </summary>
public class SearchOrchestrator : ISearchOrchestrator
{
    private readonly IInputNormalizer _inputNormalizer;
    private readonly IMaterialFamilyRepository _materialFamilyRepository;
    private readonly IProductRepository _productRepository;
    private readonly IProductEnrichedRepository _productEnrichedRepository;
    private readonly IEmbeddingService _embeddingService;
    private readonly ISearchFusionService _fusionService;
    private readonly SemanticSearchSettings _semanticSettings;
    private readonly ILogger<SearchOrchestrator> _logger;

    public SearchOrchestrator(
        IInputNormalizer inputNormalizer,
        IMaterialFamilyRepository materialFamilyRepository,
        IProductRepository productRepository,
        IProductEnrichedRepository productEnrichedRepository,
        IEmbeddingService embeddingService,
        ISearchFusionService fusionService,
        IOptions<SemanticSearchSettings> semanticSettings,
        ILogger<SearchOrchestrator> logger)
    {
        _inputNormalizer = inputNormalizer;
        _materialFamilyRepository = materialFamilyRepository;
        _productRepository = productRepository;
        _productEnrichedRepository = productEnrichedRepository;
        _embeddingService = embeddingService;
        _fusionService = fusionService;
        _semanticSettings = semanticSettings.Value;
        _logger = logger;
    }

    public async Task<SearchResult> SearchAsync(string bomText, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var warnings = new List<string>();

        _logger.LogInformation("Starting search for: {BomText}", bomText);

        // Step 1: Normalize input
        _logger.LogDebug("Step 1: Normalizing input...");
        var bomItem = _inputNormalizer.Normalize(bomText);
        _logger.LogInformation("Extracted {KeywordCount} keywords, {SizeCount} size variants, {SynonymCount} synonyms",
            bomItem.Keywords.Count, bomItem.SizeVariants.Count, bomItem.Synonyms.Count);

        // Step 2: Find material family using hybrid search
        _logger.LogDebug("Step 2: Finding material family...");
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

        // Step 3: Get CSI code from family
        var csiCode = primaryFamily?.CsiDivision != null 
            ? $"{primaryFamily.CsiDivision}2200" // Default pattern for CSI codes
            : null;
        _logger.LogDebug("Step 3: Using CSI code: {CsiCode}", csiCode ?? "none");

        // Step 4: Search products
        _logger.LogDebug("Step 4: Searching products...");
        var products = await _productRepository.FindProductsAsync(
            primaryFamily?.FamilyLabel,
            null, // Don't filter by exact CSI to get more results
            bomItem.SizeVariants.Count > 0 ? bomItem.SizeVariants : null,
            bomItem.Synonyms,
            cancellationToken);

        _logger.LogInformation("Found {ProductCount} products matching criteria", products.Count);

        // Step 5: Get enriched data in parallel
        _logger.LogDebug("Step 5: Fetching enriched product data...");
        var productIds = products.Select(p => p.ProductId).ToList();
        var enrichedData = await _productEnrichedRepository.GetEnrichedDataAsync(productIds, cancellationToken);
        
        // Create lookup for enriched data
        var enrichedLookup = enrichedData.ToDictionary(e => e.ProductId, e => e);

        // Step 6: Combine into matches
        _logger.LogDebug("Step 6: Combining results...");
        var matches = products.Select(p => CreateProductMatch(p, enrichedLookup)).ToList();

        stopwatch.Stop();
        _logger.LogInformation("Search completed in {ElapsedMs}ms with {MatchCount} matches", 
            stopwatch.ElapsedMilliseconds, matches.Count);

        return new SearchResult
        {
            Query = bomText,
            SizeVariants = bomItem.SizeVariants,
            Keywords = bomItem.Synonyms,
            FamilyLabel = primaryFamily?.FamilyLabel,
            CsiCode = products.FirstOrDefault()?.CsiSectionCode ?? csiCode,
            Matches = matches,
            ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
            Warnings = warnings
        };
    }

    /// <summary>
    /// Find material families using hybrid semantic + full-text search with RRF fusion.
    /// Falls back to keyword search if semantic search fails.
    /// </summary>
    private async Task<List<MaterialFamily>> FindMaterialFamiliesAsync(
        string queryText,
        IEnumerable<string> fallbackKeywords,
        CancellationToken cancellationToken)
    {
        if (!_semanticSettings.Enabled)
        {
            _logger.LogDebug("Semantic search disabled, using keyword search");
            return await _materialFamilyRepository.FindByKeywordsAsync(fallbackKeywords, cancellationToken);
        }

        try
        {
            // Generate embedding for the query
            _logger.LogDebug("Generating embedding for query: {Query}", queryText);
            var embedding = await _embeddingService.GenerateEmbeddingAsync(queryText, cancellationToken);

            // Clean query for FTS (strip size patterns that break websearch_to_tsquery)
            var ftsQuery = CleanQueryForFts(queryText);
            _logger.LogDebug("Cleaned FTS query: {FtsQuery}", ftsQuery);

            // Run both searches in parallel
            _logger.LogDebug("Running parallel FTS and semantic search...");
            var ftsTask = _materialFamilyRepository.FullTextSearchAsync(
                ftsQuery, 
                _semanticSettings.MatchCount * 2, 
                cancellationToken);
            var semanticTask = _materialFamilyRepository.SemanticSearchAsync(
                embedding, 
                _semanticSettings.MatchCount * 2, 
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
                _semanticSettings.FullTextWeight,
                _semanticSettings.SemanticWeight,
                _semanticSettings.RrfK,
                _semanticSettings.MatchCount);

            if (fusedResults.Count > 0)
            {
                return fusedResults;
            }

            // If hybrid search returns nothing, fallback to keyword search
            _logger.LogDebug("Hybrid search returned no results, falling back to keyword search");
            return await _materialFamilyRepository.FindByKeywordsAsync(fallbackKeywords, cancellationToken);
        }
        catch (Exception ex)
        {
            // Graceful degradation: fallback to keyword search on any error
            _logger.LogWarning(ex, "Semantic search failed, falling back to keyword search");
            return await _materialFamilyRepository.FindByKeywordsAsync(fallbackKeywords, cancellationToken);
        }
    }

    private static ProductMatch CreateProductMatch(Product product, Dictionary<Guid, ProductEnriched> enrichedLookup)
    {
        enrichedLookup.TryGetValue(product.ProductId, out var enriched);

        return new ProductMatch
        {
            ProductId = product.ProductId,
            Vendor = product.VendorName,
            ModelName = product.ModelName,
            ModelCode = enriched?.ModelCode,
            CsiCode = product.CsiSectionCode,
            UseWhen = enriched?.UseWhen,
            KeyFeatures = ParseJsonArray(enriched?.KeyFeaturesJson),
            TechnicalSpecs = ParseJsonObject(enriched?.TechnicalSpecsJson),
            PerformanceData = ParseJsonObject(enriched?.PerformanceDataJson),
            SourceSchema = enriched?.SourceSchema
        };
    }

    private static List<string>? ParseJsonArray(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json);
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, object>? ParseJsonObject(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Cleans the query for full-text search by removing size/dimension patterns
    /// that can break websearch_to_tsquery (e.g., 8" becomes a phrase start).
    /// </summary>
    private static string CleanQueryForFts(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return query;

        // Remove size patterns: 8", 8 inch, 8-inch, 20cm, 200mm, etc.
        // These break FTS because " starts a phrase and numbers aren't indexed
        var cleaned = Regex.Replace(
            query,
            @"\b\d+(\.\d+)?\s*(""|''|inch|in|inches|cm|mm|feet|ft|foot|meter|m|metres|meters)\b",
            " ",
            RegexOptions.IgnoreCase);

        // Also handle patterns like 8x8, 8x8x16, 4'x8'
        cleaned = Regex.Replace(
            cleaned,
            @"\b\d+(\.\d+)?[''′]?\s*[xX×]\s*\d+(\.\d+)?[''′]?(\s*[xX×]\s*\d+(\.\d+)?[''′]?)?\b",
            " ",
            RegexOptions.IgnoreCase);

        // Remove fractions: 5/8, 1/2, 3/4, 5/8", etc.
        cleaned = Regex.Replace(
            cleaned,
            @"\b\d+/\d+\s*(""|''|inch|in|inches|cm|mm)?\b",
            " ",
            RegexOptions.IgnoreCase);

        // Remove standalone quotes that might remain
        cleaned = Regex.Replace(cleaned, @"[""]+", " ");

        // Remove standalone numbers (not useful for FTS, break queries)
        cleaned = Regex.Replace(cleaned, @"\b\d+(\.\d+)?\b", " ");

        // Remove context/preposition words that cause AND failures
        // These words describe WHERE/HOW materials are used, not WHAT they are
        var contextWords = new[] { "on", "for", "with", "over", "under", "around", "between", "above", "below" };
        foreach (var word in contextWords)
        {
            cleaned = Regex.Replace(cleaned, $@"\b{word}\b", " ", RegexOptions.IgnoreCase);
        }

        // Normalize whitespace
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        return cleaned;
    }
}
