namespace SourcingEngine.Core.Models;

/// <summary>
/// Enriched product data from vendor-specific schemas
/// </summary>
public record ProductEnriched
{
    public Guid ProductId { get; init; }
    public string? ModelCode { get; init; }
    public string? UseWhen { get; init; }
    public string? KeyFeaturesJson { get; init; }
    public string? TechnicalSpecsJson { get; init; }
    public string? PerformanceDataJson { get; init; }
    public required string SourceSchema { get; init; }
}
