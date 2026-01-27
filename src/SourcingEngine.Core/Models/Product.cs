namespace SourcingEngine.Core.Models;

/// <summary>
/// Base product from public.products table
/// </summary>
public record Product
{
    public Guid ProductId { get; init; }
    public int VendorId { get; init; }
    public required string VendorName { get; init; }
    public required string ModelName { get; init; }
    public string? FamilyLabel { get; init; }
    public string? CsiSectionCode { get; init; }
    public bool IsActive { get; init; }
    public decimal? BasePrice { get; init; }
    public int? AverageLeadTimeDays { get; init; }
}
