using SourcingEngine.Core.Models;

namespace SourcingEngine.Core.Repositories;

/// <summary>
/// Repository interface for product lookups
/// </summary>
public interface IProductRepository
{
    /// <summary>
    /// Find products by family label, CSI code, and size patterns
    /// </summary>
    Task<List<Product>> FindProductsAsync(
        string? familyLabel,
        string? csiCode,
        IEnumerable<string>? sizePatterns,
        IEnumerable<string>? keywords,
        CancellationToken cancellationToken = default);
}
