using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SourcingEngine.Common.Models;
using SourcingEngine.Core.Configuration;
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
    private readonly int _maxConcurrentSearches;
    private readonly int _perItemTimeoutSeconds;

    /// <summary>
    /// Maximum allowed length for single-item search input text.
    /// </summary>
    private const int MaxInputLength = 500;

    public SearchOrchestrator(
        ISearchStrategy strategy,
        ILogger<SearchOrchestrator> logger,
        IOptions<AgentSettings>? agentSettings = null)
    {
        _strategy = strategy;
        _logger = logger;
        _maxConcurrentSearches = agentSettings?.Value.MaxConcurrentSearches ?? 1;
        _perItemTimeoutSeconds = agentSettings?.Value.PerItemTimeoutSeconds ?? 180;
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

        // Deduplicate BOM items by name (case-insensitive) to prevent the same item
        // from appearing in both result and zero-result queues due to duplicate entries
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uniqueItems = extraction.Items.Where(i => seen.Add(i.BomItem)).ToList();

        if (uniqueItems.Count < extraction.Items.Count)
        {
            var dupeCount = extraction.Items.Count - uniqueItems.Count;
            _logger.LogInformation(
                "Deduplicated {DuplicateCount} duplicate BOM item(s) from {OriginalCount} items",
                dupeCount, extraction.Items.Count);
            warnings.Add($"Removed {dupeCount} duplicate BOM item(s)");
        }

        // Process BOM items concurrently (bounded by MaxConcurrentSearches)
        // to reduce total latency in Lambda. Each agent search takes ~90-150s,
        // so parallel execution is critical for multi-item messages.
        var warningsLock = new object();
        var semaphore = new SemaphoreSlim(_maxConcurrentSearches);
        var tasks = uniqueItems.Select(async item =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                // Per-item timeout prevents one stuck search from consuming the full Lambda budget
                using var itemCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                itemCts.CancelAfter(TimeSpan.FromSeconds(_perItemTimeoutSeconds));

                var searchResult = await SearchItemAsync(item, itemCts.Token);

                _logger.LogInformation(
                    "BOM item '{BomItem}' → {MatchCount} matches",
                    item.BomItem, searchResult.MatchCount);

                return new BomItemSearchResult
                {
                    BomItemName = item.BomItem,
                    Description = item.Description,
                    Quantity = item.Quantity,
                    Certifications = item.Certifications,
                    Notes = item.Notes,
                    SearchResult = searchResult
                };
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Search timed out after {Timeout}s for BOM item '{BomItem}'",
                    _perItemTimeoutSeconds, item.BomItem);
                lock (warningsLock) { warnings.Add($"Search timed out for '{item.BomItem}' after {_perItemTimeoutSeconds}s"); }

                return new BomItemSearchResult
                {
                    BomItemName = item.BomItem,
                    Description = item.Description,
                    Quantity = item.Quantity,
                    Certifications = item.Certifications,
                    Notes = item.Notes,
                    SearchResult = new SearchResult
                    {
                        Query = item.Description,
                        Warnings = [$"Search timed out after {_perItemTimeoutSeconds}s"]
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Search failed for BOM item '{BomItem}'", item.BomItem);
                lock (warningsLock) { warnings.Add($"Search failed for '{item.BomItem}': {ex.Message}"); }

                return new BomItemSearchResult
                {
                    BomItemName = item.BomItem,
                    Description = item.Description,
                    Quantity = item.Quantity,
                    Certifications = item.Certifications,
                    Notes = item.Notes,
                    SearchResult = new SearchResult
                    {
                        Query = item.Description,
                        Warnings = [$"Search failed: {ex.Message}"]
                    }
                };
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        itemResults.AddRange(await Task.WhenAll(tasks));

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
        var item = new BomLineItem { BomItem = bomText, Description = bomText };

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
            Query = item.Description,
            FamilyLabel = result.FamilyLabel,
            CsiCode = result.CsiCode,
            Matches = result.Matches,
            ExecutionTimeMs = itemStopwatch.ElapsedMilliseconds,
            Warnings = result.Warnings
        };
    }
}
