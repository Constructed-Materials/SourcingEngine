using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SourcingEngine.Core.Models;
using SourcingEngine.Core.Repositories;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Main search orchestrator implementation
/// </summary>
public class SearchOrchestrator : ISearchOrchestrator
{
    private readonly IInputNormalizer _inputNormalizer;
    private readonly IMaterialFamilyRepository _materialFamilyRepository;
    private readonly IProductRepository _productRepository;
    private readonly IProductEnrichedRepository _productEnrichedRepository;
    private readonly ILogger<SearchOrchestrator> _logger;

    public SearchOrchestrator(
        IInputNormalizer inputNormalizer,
        IMaterialFamilyRepository materialFamilyRepository,
        IProductRepository productRepository,
        IProductEnrichedRepository productEnrichedRepository,
        ILogger<SearchOrchestrator> logger)
    {
        _inputNormalizer = inputNormalizer;
        _materialFamilyRepository = materialFamilyRepository;
        _productRepository = productRepository;
        _productEnrichedRepository = productEnrichedRepository;
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

        // Step 2: Find material family
        _logger.LogDebug("Step 2: Finding material family...");
        var families = await _materialFamilyRepository.FindByKeywordsAsync(bomItem.Synonyms, cancellationToken);
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
}
