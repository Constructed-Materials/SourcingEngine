using SourcingEngine.Core.Models;

namespace SourcingEngine.Core.Repositories;

/// <summary>
/// Represents a ranked search result with score for fusion
/// </summary>
public record RankedMaterialFamily(MaterialFamily Family, int Rank, float Score);

/// <summary>
/// Repository interface for material family lookups
/// </summary>
public interface IMaterialFamilyRepository
{
    /// <summary>
    /// Find material families matching the given keywords (legacy method)
    /// </summary>
    Task<List<MaterialFamily>> FindByKeywordsAsync(
        IEnumerable<string> keywords, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Full-text search on family_label, family_name, and synonyms columns.
    /// Returns ranked results ordered by ts_rank_cd score.
    /// </summary>
    /// <param name="queryText">The search query text</param>
    /// <param name="maxResults">Maximum number of results to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Ranked list of material families with FTS scores</returns>
    Task<List<RankedMaterialFamily>> FullTextSearchAsync(
        string queryText,
        int maxResults = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Semantic vector search using embedding similarity.
    /// Returns ranked results ordered by cosine distance.
    /// </summary>
    /// <param name="embedding">The query embedding vector (384 dimensions)</param>
    /// <param name="maxResults">Maximum number of results to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Ranked list of material families with similarity scores</returns>
    Task<List<RankedMaterialFamily>> SemanticSearchAsync(
        float[] embedding,
        int maxResults = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the embedding vector for a material family
    /// </summary>
    /// <param name="familyLabel">The family_label primary key</param>
    /// <param name="embedding">The embedding vector to store</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateEmbeddingAsync(
        string familyLabel,
        float[] embedding,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all material families (for seeding embeddings)
    /// </summary>
    Task<List<MaterialFamily>> GetAllAsync(CancellationToken cancellationToken = default);
}
