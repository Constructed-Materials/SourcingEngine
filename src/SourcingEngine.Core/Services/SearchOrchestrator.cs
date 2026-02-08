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
    private readonly ISemanticProductRepository? _semanticProductRepository;
    private readonly IQueryParserService? _queryParserService;
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
        ILogger<SearchOrchestrator> logger,
        ISemanticProductRepository? semanticProductRepository = null,
        IQueryParserService? queryParserService = null)
    {
        _inputNormalizer = inputNormalizer;
        _materialFamilyRepository = materialFamilyRepository;
        _productRepository = productRepository;
        _productEnrichedRepository = productEnrichedRepository;
        _semanticProductRepository = semanticProductRepository;
        _queryParserService = queryParserService;
        _embeddingService = embeddingService;
        _fusionService = fusionService;
        _semanticSettings = semanticSettings.Value;
        _logger = logger;
    }

    public Task<SearchResult> SearchAsync(string bomText, CancellationToken cancellationToken = default)
    {
        var mode = _semanticSettings.Enabled ? _semanticSettings.DefaultMode : SemanticSearchMode.Off;
        return SearchAsync(bomText, mode, cancellationToken);
    }

    public async Task<SearchResult> SearchAsync(string bomText, SemanticSearchMode mode, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var warnings = new List<string>();

        _logger.LogInformation("Starting search for: {BomText} (mode: {Mode})", bomText, mode);

        // Step 1: Normalize input
        _logger.LogDebug("Step 1: Normalizing input...");
        var bomItem = _inputNormalizer.Normalize(bomText);
        _logger.LogInformation("Extracted {KeywordCount} keywords, {SizeCount} size variants, {SynonymCount} synonyms",
            bomItem.Keywords.Count, bomItem.SizeVariants.Count, bomItem.Synonyms.Count);

        List<ProductMatch> matches;
        MaterialFamily? primaryFamily = null;
        string? csiCode = null;

        // Route based on semantic search mode
        if (mode == SemanticSearchMode.ProductFirst && _semanticProductRepository != null)
        {
            // ProductFirst: Direct semantic search on products with enrichment
            var (productMatches, productWarnings, productFamily, productCsi) = await SearchProductsSemanticAsync(bomText, cancellationToken);
            matches = productMatches;
            warnings.AddRange(productWarnings);
            // Derive family/CSI from semantic results
            if (productFamily != null)
            {
                primaryFamily = (await _materialFamilyRepository.FindByKeywordsAsync(
                    new[] { productFamily }, cancellationToken)).FirstOrDefault();
            }
            csiCode = productCsi;
        }
        else if (mode == SemanticSearchMode.Hybrid && _semanticProductRepository != null)
        {
            // Hybrid: Run both FamilyFirst and ProductFirst and fuse results
            var familyFirstTask = SearchFamilyFirstAsync(bomText, bomItem, cancellationToken);
            var productFirstTask = SearchProductsSemanticAsync(bomText, cancellationToken);

            await Task.WhenAll(familyFirstTask, productFirstTask);

            var (familyMatches, familyWarnings, family, code) = await familyFirstTask;
            var (productMatches, productWarnings, _, _) = await productFirstTask;

            primaryFamily = family;
            csiCode = code;

            // Fuse results using similarity scores
            matches = FuseProductMatches(familyMatches, productMatches, _semanticSettings.MatchCount);
            warnings.AddRange(familyWarnings);
            warnings.AddRange(productWarnings);

            _logger.LogInformation("Hybrid search: {FamilyCount} family matches, {ProductCount} semantic matches, {FusedCount} fused",
                familyMatches.Count, productMatches.Count, matches.Count);
        }
        else
        {
            // FamilyFirst or Off: Original family-based search
            var (familyMatches, familyWarnings, family, code) = await SearchFamilyFirstAsync(bomText, bomItem, cancellationToken);
            matches = familyMatches;
            warnings.AddRange(familyWarnings);
            primaryFamily = family;
            csiCode = code;
        }

        stopwatch.Stop();
        _logger.LogInformation("Search completed in {ElapsedMs}ms with {MatchCount} matches",
            stopwatch.ElapsedMilliseconds, matches.Count);

        return new SearchResult
        {
            Query = bomText,
            SizeVariants = bomItem.SizeVariants,
            Keywords = bomItem.Synonyms,
            FamilyLabel = primaryFamily?.FamilyLabel,
            CsiCode = csiCode,
            Matches = matches,
            ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
            Warnings = warnings
        };
    }

    /// <summary>
    /// Original family-first search approach
    /// </summary>
    private async Task<(List<ProductMatch> Matches, List<string> Warnings, MaterialFamily? Family, string? CsiCode)> 
        SearchFamilyFirstAsync(string bomText, BomItem bomItem, CancellationToken cancellationToken)
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

        var matches = products.Select(p => CreateProductMatch(p, enrichedLookup)).ToList();

        return (matches, warnings, primaryFamily, products.FirstOrDefault()?.CsiSectionCode ?? csiCode);
    }

    /// <summary>
    /// Direct semantic search on products (ProductFirst mode)
    /// Returns enriched ProductMatch with all available data (same richness as FamilyFirst)
    /// </summary>
    private async Task<(List<ProductMatch> Matches, List<string> Warnings, string? FamilyLabel, string? CsiCode)> 
        SearchProductsSemanticAsync(string bomText, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        if (_semanticProductRepository == null)
        {
            warnings.Add("Semantic product repository not available");
            return (new List<ProductMatch>(), warnings, null, null);
        }

        try
        {
            // Use LLM query parser to enrich the BOM text before embedding
            var textToEmbed = bomText;
            if (_queryParserService != null)
            {
                try
                {
                    var parsed = await _queryParserService.ParseAsync(bomText, cancellationToken);
                    if (parsed.Success && !string.IsNullOrWhiteSpace(parsed.SearchQuery))
                    {
                        textToEmbed = parsed.SearchQuery;
                        _logger.LogInformation(
                            "LLM parsed '{OriginalText}' → '{EnrichedQuery}' (confidence: {Confidence:F2})",
                            bomText, textToEmbed, parsed.Confidence);
                    }
                    else
                    {
                        _logger.LogDebug("Query parser returned no enriched query, using raw text");
                    }
                }
                catch (Exception parseEx)
                {
                    _logger.LogWarning(parseEx, "Query parsing failed, falling back to raw BOM text");
                }
            }

            // Generate embedding for the (possibly enriched) query
            var embedding = await _embeddingService.GenerateEmbeddingAsync(textToEmbed, cancellationToken);

            // Search products by semantic similarity
            var semanticMatches = await _semanticProductRepository.SearchByEmbeddingAsync(
                embedding,
                _semanticSettings.SimilarityThreshold,
                _semanticSettings.MatchCount,
                cancellationToken);

            _logger.LogInformation("Semantic search found {Count} products above threshold {Threshold}",
                semanticMatches.Count, _semanticSettings.SimilarityThreshold);

            // Derive family label from most common result
            var familyLabel = semanticMatches
                .Where(sm => !string.IsNullOrWhiteSpace(sm.FamilyLabel))
                .GroupBy(sm => sm.FamilyLabel)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key;

            // Derive CSI code from first non-null result
            var csiCode = semanticMatches
                .FirstOrDefault(sm => !string.IsNullOrWhiteSpace(sm.CsiCode))?.CsiCode;

            // Enrich with vendor-specific data (same as FamilyFirst uses)
            var productIds = semanticMatches.Select(sm => sm.ProductId).ToList();
            var enrichedData = await _productEnrichedRepository.GetEnrichedDataAsync(productIds, cancellationToken);
            var enrichedLookup = enrichedData.ToDictionary(e => e.ProductId, e => e);

            _logger.LogDebug("Enriched {EnrichedCount}/{TotalCount} semantic matches with vendor data",
                enrichedData.Count, semanticMatches.Count);

            // Convert to fully-populated ProductMatch format
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
                    KeyFeatures = ParseJsonArray(enriched?.KeyFeaturesJson),
                    TechnicalSpecs = ParseJsonObject(enriched?.TechnicalSpecsJson),
                    PerformanceData = ParseJsonObject(enriched?.PerformanceDataJson),
                    SourceSchema = enriched?.SourceSchema,
                    SemanticScore = sm.Similarity
                };
            }).ToList();

            return (matches, warnings, familyLabel, csiCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Semantic product search failed");
            warnings.Add($"Semantic search failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Fuse results from family-first and product-first searches
    /// </summary>
    private List<ProductMatch> FuseProductMatches(
        List<ProductMatch> familyMatches,
        List<ProductMatch> semanticMatches,
        int limit)
    {
        // Create lookup for deduplication
        var seen = new HashSet<string>();
        var results = new List<ProductMatch>();

        // Interleave results, preferring semantic matches (they have similarity scores)
        var semanticQueue = new Queue<ProductMatch>(semanticMatches);
        var familyQueue = new Queue<ProductMatch>(familyMatches);

        while (results.Count < limit && (semanticQueue.Count > 0 || familyQueue.Count > 0))
        {
            // Take from semantic first (higher confidence)
            if (semanticQueue.Count > 0)
            {
                var match = semanticQueue.Dequeue();
                var key = $"{match.Vendor}:{match.ModelName}";
                if (seen.Add(key))
                {
                    results.Add(match);
                }
            }

            // Then take from family results
            if (familyQueue.Count > 0 && results.Count < limit)
            {
                var match = familyQueue.Dequeue();
                var key = $"{match.Vendor}:{match.ModelName}";
                if (seen.Add(key))
                {
                    results.Add(match);
                }
            }
        }

        return results;
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
