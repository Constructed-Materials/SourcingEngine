namespace SourcingEngine.Core.Models;

/// <summary>
/// Material family from cm_master_materials table
/// </summary>
public record MaterialFamily
{
    /// <summary>
    /// Primary key - e.g., "cmu_blocks"
    /// </summary>
    public required string FamilyLabel { get; init; }
    
    /// <summary>
    /// Display name - e.g., "Concrete Masonry Units"
    /// </summary>
    public string? FamilyName { get; init; }
    
    /// <summary>
    /// CSI division code - e.g., "04"
    /// </summary>
    public string? CsiDivision { get; init; }
    
    /// <summary>
    /// Typical lead time in days
    /// </summary>
    public int? TypicalLeadTimeDays { get; init; }
}
