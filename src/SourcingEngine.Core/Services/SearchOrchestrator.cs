using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SourcingEngine.Core.Configuration;
using SourcingEngine.Core.Models;
using SourcingEngine.Core.Repositories;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Thin orchestrator that validates input, normalizes the BOM text,
/// selects the appropriate <see cref="ISearchStrategy"/>, and assembles
/// the final <see cref="SearchResult"/>.
/// All search logic lives in the strategy implementations.
/// </summary>
public class SearchOrchestrator : ISearchOrchestrator
{
    private readonly IInputNormalizer _inputNormalizer;
    private readonly IMaterialFamilyRepository _materialFamilyRepository;
    private readonly IReadOnlyDictionary<SemanticSearchMode, ISearchStrategy> _strategies;
    private readonly SemanticSearchSettings _semanticSettings;
    private readonly ILogger<SearchOrchestrator> _logger;

    /// <summary>
    /// Maximum allowed length for search input text.
    /// </summary>
    private const int MaxInputLength = 500;

    public SearchOrchestrator(
        IInputNormalizer inputNormalizer,
        IMaterialFamilyRepository materialFamilyRepository,
        IEnumerable<ISearchStrategy> strategies,
        IOptions<SemanticSearchSettings> semanticSettings,
        ILogger<SearchOrchestrator> logger)
    {
        _inputNormalizer = inputNormalizer;
        _materialFamilyRepository = materialFamilyRepository;
        _strategies = strategies.ToDictionary(s => s.Mode, s => s);
        _semanticSettings = semanticSettings.Value;
        _logger = logger;
    }

    public Task<SearchResult> SearchAsync(string bomText, CancellationToken cancellationToken = default)
    {
        var mode = _semanticSettings.Enabled ? _semanticSettings.DefaultMode : SemanticSearchMode.Off;
        return SearchAsync(bomText, mode, cancellationToken);
    }

    public async Task<SearchResult> SearchAsync(
        string bomText, SemanticSearchMode mode, CancellationToken cancellationToken = default)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(bomText))
            throw new ArgumentException("Search text cannot be null or empty.", nameof(bomText));

        bomText = bomText.Trim();

        if (bomText.Length > MaxInputLength)
            throw new ArgumentException(
                $"Search text exceeds maximum length of {MaxInputLength} characters.", nameof(bomText));

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Starting search for: {BomText} (mode: {Mode})", bomText, mode);

        // Step 1: Normalize input
        var bomItem = _inputNormalizer.Normalize(bomText);
        _logger.LogInformation(
            "Extracted {KeywordCount} keywords, {SizeCount} size variants, {SynonymCount} synonyms",
            bomItem.Keywords.Count, bomItem.SizeVariants.Count, bomItem.Synonyms.Count);

        // Step 2: Select strategy (fall back to FamilyFirst / Off when the requested mode is unavailable)
        var effectiveMode = _strategies.ContainsKey(mode) ? mode : SemanticSearchMode.FamilyFirst;
        if (!_strategies.ContainsKey(effectiveMode))
            effectiveMode = SemanticSearchMode.Off;

        var strategy = _strategies[effectiveMode];
        _logger.LogDebug("Using strategy: {Strategy} (requested: {Requested})",
            strategy.GetType().Name, mode);

        // Step 3: Execute
        var result = await strategy.ExecuteAsync(bomText, bomItem, cancellationToken);

        // Step 4: Resolve family object for label (ProductFirst only returns string)
        MaterialFamily? primaryFamily = null;
        if (result.FamilyLabel != null)
        {
            primaryFamily = (await _materialFamilyRepository.FindByKeywordsAsync(
                new[] { result.FamilyLabel }, cancellationToken)).FirstOrDefault();
        }

        stopwatch.Stop();
        _logger.LogInformation("Search completed in {ElapsedMs}ms with {MatchCount} matches",
            stopwatch.ElapsedMilliseconds, result.Matches.Count);

        return new SearchResult
        {
            Query = bomText,
            SizeVariants = bomItem.SizeVariants,
            Keywords = bomItem.Synonyms,
            FamilyLabel = primaryFamily?.FamilyLabel ?? result.FamilyLabel,
            CsiCode = result.CsiCode,
            Matches = result.Matches,
            ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
            Warnings = result.Warnings
        };
    }
}
