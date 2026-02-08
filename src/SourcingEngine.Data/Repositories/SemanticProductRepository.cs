using System.Globalization;
using Microsoft.Extensions.Logging;
using Npgsql;
using SourcingEngine.Core.Repositories;

namespace SourcingEngine.Data.Repositories;

/// <summary>
/// Repository for semantic vector search on products using pgvector
/// </summary>
public class SemanticProductRepository : ISemanticProductRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<SemanticProductRepository> _logger;

    public SemanticProductRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<SemanticProductRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<List<SemanticProductMatch>> SearchByEmbeddingAsync(
        float[] queryEmbedding,
        float matchThreshold = 0.5f,
        int matchCount = 10,
        CancellationToken cancellationToken = default)
    {
        return await SearchByEmbeddingAsync(queryEmbedding, null, matchThreshold, matchCount, cancellationToken);
    }

    public async Task<List<SemanticProductMatch>> SearchByEmbeddingAsync(
        float[] queryEmbedding,
        string? familyLabel,
        float matchThreshold = 0.5f,
        int matchCount = 10,
        CancellationToken cancellationToken = default)
    {
        if (queryEmbedding == null || queryEmbedding.Length == 0)
        {
            throw new ArgumentException("Query embedding cannot be null or empty", nameof(queryEmbedding));
        }

        _logger.LogDebug(
            "Semantic search with {Dimensions}d embedding, threshold={Threshold}, limit={Limit}",
            queryEmbedding.Length, matchThreshold, matchCount);

        var results = new List<SemanticProductMatch>();

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

            // Build SQL query with JOINs to vendors and product_knowledge
            var whereConditions = new List<string>
            {
                "p.embedding IS NOT NULL",
                "p.is_active = true",
                "1 - (p.embedding <=> @query_embedding::vector) > @threshold"
            };

            if (!string.IsNullOrWhiteSpace(familyLabel))
            {
                whereConditions.Add("p.family_label = @family_label");
            }

            var sql = $@"
                SELECT 
                    p.product_id,
                    v.name AS vendor_name,
                    p.model_name,
                    p.family_label,
                    p.csi_section_code,
                    pk.description,
                    pk.use_cases::text,
                    pk.specifications::text,
                    p.embedding_text,
                    1 - (p.embedding <=> @query_embedding::vector) AS similarity
                FROM public.products p
                JOIN public.vendors v ON p.vendor_id = v.vendor_id
                LEFT JOIN public.product_knowledge pk ON p.product_id = pk.product_id
                WHERE {string.Join(" AND ", whereConditions)}
                ORDER BY p.embedding <=> @query_embedding::vector
                LIMIT @match_count";

            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            // Add parameters
            AddVectorParameter(command, "query_embedding", queryEmbedding);
            
            var thresholdParam = command.CreateParameter();
            thresholdParam.ParameterName = "threshold";
            thresholdParam.Value = matchThreshold;
            command.Parameters.Add(thresholdParam);

            var limitParam = command.CreateParameter();
            limitParam.ParameterName = "match_count";
            limitParam.Value = matchCount;
            command.Parameters.Add(limitParam);

            if (!string.IsNullOrWhiteSpace(familyLabel))
            {
                var typeParam = command.CreateParameter();
                typeParam.ParameterName = "family_label";
                typeParam.Value = familyLabel;
                command.Parameters.Add(typeParam);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new SemanticProductMatch
                {
                    ProductId = reader.GetGuid(0),
                    VendorName = reader.IsDBNull(1) ? "Unknown" : reader.GetString(1),
                    ModelName = reader.IsDBNull(2) ? "Unknown" : reader.GetString(2),
                    FamilyLabel = reader.IsDBNull(3) ? null : reader.GetString(3),
                    CsiCode = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Description = reader.IsDBNull(5) ? null : reader.GetString(5),
                    UseCases = reader.IsDBNull(6) ? null : reader.GetString(6),
                    SpecificationsJson = reader.IsDBNull(7) ? null : reader.GetString(7),
                    EmbeddingText = reader.IsDBNull(8) ? null : reader.GetString(8),
                    Similarity = reader.GetFloat(9)
                });
            }

            _logger.LogInformation(
                "Semantic search returned {Count} results above threshold {Threshold}",
                results.Count, matchThreshold);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Semantic product search failed");
            throw;
        }
    }

    private static void AddVectorParameter(System.Data.Common.DbCommand command, string name, float[] vector)
    {
        // Use string literal representation — compatible with all Npgsql versions
        // SQL uses ::vector cast to convert the string to a pgvector type
        var vectorLiteral = "[" + string.Join(",", vector.Select(f => f.ToString("G9", CultureInfo.InvariantCulture))) + "]";
        var param = command.CreateParameter();
        param.ParameterName = name;
        param.Value = vectorLiteral;
        command.Parameters.Add(param);
    }
}
