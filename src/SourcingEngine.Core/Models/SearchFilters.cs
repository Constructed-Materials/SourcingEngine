using System.Text.Json;

namespace SourcingEngine.Core.Models;

/// <summary>
/// Structured filters for hybrid search — applied as WHERE-clause conditions
/// during the pgvector similarity search (inline filtering).
/// Supports family label, vendor name, and JSONB specification filters.
/// </summary>
public record SearchFilters
{
    /// <summary>
    /// Material family label to filter by (e.g., "cmu_blocks", "aluminum_windows").
    /// Applied as: <c>p.family_label = @family_label</c>
    /// </summary>
    public string? FamilyLabel { get; init; }

    /// <summary>
    /// Vendor/manufacturer name to filter by.
    /// Applied as: <c>v.name = @vendor_name</c>
    /// </summary>
    public string? VendorName { get; init; }

    /// <summary>
    /// JSONB containment filters on product_knowledge.specifications.
    /// Each entry is applied as: <c>pk.specifications @&gt; @spec_filter_N::jsonb</c>
    /// Example: <c>{"stone_type": "Marble"}</c> filters to only marble products.
    /// </summary>
    public List<string>? SpecificationContainmentFilters { get; init; }

    /// <summary>
    /// Whether any non-null filters are present.
    /// </summary>
    public bool HasFilters =>
        !string.IsNullOrWhiteSpace(FamilyLabel) ||
        !string.IsNullOrWhiteSpace(VendorName) ||
        (SpecificationContainmentFilters != null && SpecificationContainmentFilters.Count > 0);
}
