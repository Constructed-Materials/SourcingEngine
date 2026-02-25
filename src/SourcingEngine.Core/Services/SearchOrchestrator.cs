using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SourcingEngine.Common.Models;
using SourcingEngine.Core.Models;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Orchestrator that accepts full BOM extraction results, iterates over each
/// BOM line item, invokes the <see cref="ISearchStrategy"/> for product search,
/// and aggregates into a <see cref="SourcingResult"/>.
/// </summary>
public class SearchOrchestrator : ISearchOrchestrator
{
    private readonly ISearchStrategy _strategy;
    private readonly ILogger<SearchOrchestrator> _logger;

    /// <summary>
    /// Maximum allowed length for single-item search input text.
    /// </summary>
    private const int MaxInputLength = 500;

    public SearchOrchestrator(
        ISearchStrategy strategy,
        ILogger<SearchOrchestrator> logger)
    {
        _strategy = strategy;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SourcingResult> SearchAsync(SourcingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ExtractionResult);

        var extraction = request.ExtractionResult;
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Starting sourcing for trace={TraceId}, project={ProjectId}, file={SourceFile}, items={ItemCount}",
            extraction.TraceId, extraction.ProjectId, extraction.SourceFile, extraction.Items.Count);

        var itemResults = new List<BomItemSearchResult>();
        var warnings = new List<string>(extraction.Warnings);

        foreach (var item in extraction.Items)
        {
            try
            {
                var searchResult = await SearchItemAsync(item, cancellationToken);

                itemResults.Add(new BomItemSearchResult
                {
                    BomItemName = item.BomItem,
                    Spec = item.Spec,
                    Quantity = item.Quantity,
                    SearchResult = searchResult
                });

                _logger.LogInformation(
                    "BOM item '{BomItem}' → {MatchCount} matches",
                    item.BomItem, searchResult.MatchCount);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Search failed for BOM item '{BomItem}'", item.BomItem);
                warnings.Add($"Search failed for '{item.BomItem}': {ex.Message}");

                // Add empty result so the item isn't silently dropped
                itemResults.Add(new BomItemSearchResult
                {
                    BomItemName = item.BomItem,
                    Spec = item.Spec,
                    Quantity = item.Quantity,
                    SearchResult = new SearchResult
                    {
                        Query = item.Spec,
                        Warnings = [$"Search failed: {ex.Message}"]
                    }
                });
            }
        }

        stopwatch.Stop();

        _logger.LogInformation(
            "Sourcing completed in {ElapsedMs}ms: {ItemCount} items, {TotalMatches} total matches",
            stopwatch.ElapsedMilliseconds, itemResults.Count,
            itemResults.Sum(r => r.SearchResult.MatchCount));

        return new SourcingResult
        {
            TraceId = extraction.TraceId,
            ProjectId = extraction.ProjectId,
            SourceFile = extraction.SourceFile,
            Items = itemResults,
            TotalExecutionTimeMs = stopwatch.ElapsedMilliseconds,
            Warnings = warnings
        };
    }

    /// <inheritdoc />
    public async Task<SearchResult> SearchAsync(string bomText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bomText))
            throw new ArgumentException("Search text cannot be null or empty.", nameof(bomText));

        bomText = bomText.Trim();

        if (bomText.Length > MaxInputLength)
            throw new ArgumentException(
                $"Search text exceeds maximum length of {MaxInputLength} characters.", nameof(bomText));

        // Wrap single text into a 1-item SourcingRequest
        var item = new BomLineItem { BomItem = bomText, Spec = bomText };

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Starting single-item search for: {BomText}", bomText);

        var searchResult = await SearchItemAsync(item, cancellationToken);

        stopwatch.Stop();
        _logger.LogInformation("Search completed in {ElapsedMs}ms with {MatchCount} matches",
            stopwatch.ElapsedMilliseconds, searchResult.MatchCount);

        return searchResult with { ExecutionTimeMs = stopwatch.ElapsedMilliseconds };
    }

    /// <summary>
    /// Search for a single BOM line item using the configured strategy.
    /// </summary>
    private async Task<SearchResult> SearchItemAsync(BomLineItem item, CancellationToken cancellationToken)
    {
        var itemStopwatch = Stopwatch.StartNew();

        var result = await _strategy.ExecuteAsync(item, cancellationToken);

        itemStopwatch.Stop();

        return new SearchResult
        {
            Query = item.Spec,
            FamilyLabel = result.FamilyLabel,
            CsiCode = result.CsiCode,
            Matches = result.Matches,
            ExecutionTimeMs = itemStopwatch.ElapsedMilliseconds,
            Warnings = result.Warnings
        };
    }
}
