using Microsoft.Extensions.Logging;
using Npgsql;
using Pgvector;
using SourcingEngine.Core.Models;
using SourcingEngine.Core.Repositories;
using SourcingEngine.Core.Services;

namespace SourcingEngine.Data.Repositories;

/// <summary>
/// Material family repository implementation with semantic search support
/// </summary>
public class MaterialFamilyRepository : IMaterialFamilyRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<MaterialFamilyRepository> _logger;

    public MaterialFamilyRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<MaterialFamilyRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<List<MaterialFamily>> FindByKeywordsAsync(
        IEnumerable<string> keywords, 
        CancellationToken cancellationToken = default)
    {
        var keywordList = keywords.ToList();
        if (keywordList.Count == 0)
        {
            return [];
        }

        // Build ILIKE conditions for each keyword
        var conditions = keywordList
            .Select((_, i) => $"(family_label ILIKE @p{i} OR family_name ILIKE @p{i})")
            .ToList();

        var sql = $@"
            SELECT DISTINCT family_label, family_name, csi_division, typical_lead_time_days
            FROM public.cm_master_materials
            WHERE {string.Join(" OR ", conditions)}
            ORDER BY family_label";

        var families = new List<MaterialFamily>();

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            for (int i = 0; i < keywordList.Count; i++)
            {
                var param = command.CreateParameter();
                param.ParameterName = $"p{i}";
                param.Value = $"%{keywordList[i]}%";
                command.Parameters.Add(param);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                families.Add(MapMaterialFamily(reader));
            }

            _logger.LogDebug("Found {Count} material families for keywords: {Keywords}", 
                families.Count, string.Join(", ", keywordList));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find material families for keywords: {Keywords}", 
                string.Join(", ", keywordList));
            throw;
        }

        return families;
    }

    public async Task<List<RankedMaterialFamily>> FullTextSearchAsync(
        string queryText,
        int maxResults = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return [];
        }

        // Use websearch_to_tsquery for natural language query parsing
        var sql = @"
            SELECT 
                family_label, 
                family_name, 
                csi_division, 
                typical_lead_time_days,
                ts_rank_cd(fts, websearch_to_tsquery('english', @query)) as score
            FROM public.cm_master_materials
            WHERE fts @@ websearch_to_tsquery('english', @query)
            ORDER BY score DESC
            LIMIT @limit";

        var results = new List<RankedMaterialFamily>();

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            var queryParam = command.CreateParameter();
            queryParam.ParameterName = "query";
            queryParam.Value = queryText;
            command.Parameters.Add(queryParam);

            var limitParam = command.CreateParameter();
            limitParam.ParameterName = "limit";
            limitParam.Value = maxResults;
            command.Parameters.Add(limitParam);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            int rank = 1;
            while (await reader.ReadAsync(cancellationToken))
            {
                var family = MapMaterialFamily(reader);
                var score = reader.GetFloat(4);
                results.Add(new RankedMaterialFamily(family, rank++, score));
            }

            _logger.LogDebug("Full-text search for '{Query}' returned {Count} results", 
                queryText, results.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Full-text search failed for query: {Query}", queryText);
            throw;
        }

        return results;
    }

    public async Task<List<RankedMaterialFamily>> SemanticSearchAsync(
        float[] embedding,
        int maxResults = 20,
        CancellationToken cancellationToken = default)
    {
        if (embedding == null || embedding.Length == 0)
        {
            return [];
        }

        // Use cosine distance operator (<=>)
        // Lower distance = more similar
        // Pass embedding as string representation for pgvector
        var sql = @"
            SELECT 
                family_label, 
                family_name, 
                csi_division, 
                typical_lead_time_days,
                embedding <=> @embedding::vector as distance
            FROM public.cm_master_materials
            WHERE embedding IS NOT NULL
            ORDER BY distance ASC
            LIMIT @limit";

        var results = new List<RankedMaterialFamily>();

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            // Convert float array to PostgreSQL vector string format: '[0.1,0.2,0.3,...]'
            var vectorString = EmbeddingUtilities.FormatPgVector(embedding);
            var embeddingParam = command.CreateParameter();
            embeddingParam.ParameterName = "embedding";
            embeddingParam.Value = vectorString;
            command.Parameters.Add(embeddingParam);

            var limitParam = command.CreateParameter();
            limitParam.ParameterName = "limit";
            limitParam.Value = maxResults;
            command.Parameters.Add(limitParam);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            int rank = 1;
            while (await reader.ReadAsync(cancellationToken))
            {
                var family = MapMaterialFamily(reader);
                var distance = reader.GetFloat(4);
                // Convert distance to similarity score (1 - distance for cosine)
                var score = 1.0f - distance;
                results.Add(new RankedMaterialFamily(family, rank++, score));
            }

            _logger.LogDebug("Semantic search returned {Count} results", results.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Semantic search failed");
            throw;
        }

        return results;
    }

    public async Task UpdateEmbeddingAsync(
        string familyLabel,
        float[] embedding,
        CancellationToken cancellationToken = default)
    {
        var sql = @"
            UPDATE public.cm_master_materials
            SET embedding = @embedding::vector
            WHERE family_label = @familyLabel";

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            // Convert float array to PostgreSQL vector string format: '[0.1,0.2,0.3,...]'
            var vectorString = "[" + string.Join(",", embedding.Select(f => f.ToString("G9", System.Globalization.CultureInfo.InvariantCulture))) + "]";
            var embeddingParam = command.CreateParameter();
            embeddingParam.ParameterName = "embedding";
            embeddingParam.Value = vectorString;
            command.Parameters.Add(embeddingParam);

            var labelParam = command.CreateParameter();
            labelParam.ParameterName = "familyLabel";
            labelParam.Value = familyLabel;
            command.Parameters.Add(labelParam);

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            
            if (rowsAffected == 0)
            {
                _logger.LogWarning("No rows updated for family_label: {FamilyLabel}", familyLabel);
            }
            else
            {
                _logger.LogDebug("Updated embedding for family_label: {FamilyLabel}", familyLabel);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update embedding for family_label: {FamilyLabel}", familyLabel);
            throw;
        }
    }

    public async Task<List<MaterialFamily>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var sql = @"
            SELECT family_label, family_name, csi_division, typical_lead_time_days, synonyms
            FROM public.cm_master_materials
            ORDER BY family_label";

        var families = new List<MaterialFamily>();

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                families.Add(MapMaterialFamily(reader, synonymsOrdinal: 4));
            }

            _logger.LogDebug("Retrieved {Count} material families", families.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve all material families");
            throw;
        }

        return families;
    }

    /// <summary>
    /// Maps the first 4 columns of a reader row to a MaterialFamily.
    /// Columns: family_label, family_name, csi_division, typical_lead_time_days
    /// Optionally reads Synonyms from the specified column ordinal.
    /// </summary>
    private static MaterialFamily MapMaterialFamily(System.Data.Common.DbDataReader reader, int? synonymsOrdinal = null)
    {
        return new MaterialFamily
        {
            FamilyLabel = reader.GetString(0),
            FamilyName = reader.IsDBNull(1) ? null : reader.GetString(1),
            CsiDivision = reader.IsDBNull(2) ? null : reader.GetString(2),
            TypicalLeadTimeDays = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            Synonyms = synonymsOrdinal.HasValue && !reader.IsDBNull(synonymsOrdinal.Value) 
                ? reader.GetString(synonymsOrdinal.Value) 
                : null
        };
    }
}
