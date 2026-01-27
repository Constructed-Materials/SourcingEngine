using SourcingEngine.Core.Models;

namespace SourcingEngine.Core.Repositories;

/// <summary>
/// Repository interface for material family lookups
/// </summary>
public interface IMaterialFamilyRepository
{
    /// <summary>
    /// Find material families matching the given keywords
    /// </summary>
    Task<List<MaterialFamily>> FindByKeywordsAsync(
        IEnumerable<string> keywords, 
        CancellationToken cancellationToken = default);
}
