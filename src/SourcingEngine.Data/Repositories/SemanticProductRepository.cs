using System.Globalization;
using Microsoft.Extensions.Logging;
using Npgsql;
using SourcingEngine.Core.Models;
using SourcingEngine.Core.Repositories;
using SourcingEngine.Core.Services;

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
        float matchThreshold = 0.3f,
        int matchCount = 20,
        CancellationToken cancellationToken = default)
    {
        return await SearchByEmbeddingAsync(queryEmbedding, (SearchFilters?)null, matchThreshold, matchCount, cancellationToken);
    }

    public async Task<List<SemanticProductMatch>> SearchByEmbeddingAsync(
        float[] queryEmbedding,
        string? familyLabel,
        float matchThreshold = 0.3f,
        int matchCount = 20,
        CancellationToken cancellationToken = default)
    {
        // Delegate to the new filters-based overload, converting familyLabel to SearchFilters
        var filters = string.IsNullOrWhiteSpace(familyLabel)
            ? null
            : new SearchFilters { FamilyLabel = familyLabel };

        return await SearchByEmbeddingAsync(queryEmbedding, filters, matchThreshold, matchCount, cancellationToken);
    }

    public async Task<List<SemanticProductMatch>> SearchByEmbeddingAsync(
        float[] queryEmbedding,
        SearchFilters? filters,
        float matchThreshold = 0.3f,
        int matchCount = 20,
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

            // Inline structured filters (hybrid search)
            if (!string.IsNullOrWhiteSpace(filters?.FamilyLabel))
            {
                whereConditions.Add("p.family_label = @family_label");
            }

            if (!string.IsNullOrWhiteSpace(filters?.VendorName))
            {
                whereConditions.Add("v.name = @vendor_name");
            }

            // JSONB containment filters on product_knowledge.specifications
            var specFilterParams = new List<(string ParamName, string Value)>();
            if (filters?.SpecificationContainmentFilters != null)
            {
                for (var i = 0; i < filters.SpecificationContainmentFilters.Count; i++)
                {
                    var paramName = $"spec_filter_{i}";
                    whereConditions.Add($"pk.specifications @> @{paramName}::jsonb");
                    specFilterParams.Add((paramName, filters.SpecificationContainmentFilters[i]));
                }
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

            if (!string.IsNullOrWhiteSpace(filters?.FamilyLabel))
            {
                var typeParam = command.CreateParameter();
                typeParam.ParameterName = "family_label";
                typeParam.Value = filters.FamilyLabel;
                command.Parameters.Add(typeParam);
            }

            if (!string.IsNullOrWhiteSpace(filters?.VendorName))
            {
                var vendorParam = command.CreateParameter();
                vendorParam.ParameterName = "vendor_name";
                vendorParam.Value = filters.VendorName;
                command.Parameters.Add(vendorParam);
            }

            foreach (var (paramName, value) in specFilterParams)
            {
                var specParam = command.CreateParameter();
                specParam.ParameterName = paramName;
                specParam.Value = value;
                command.Parameters.Add(specParam);
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
        var vectorLiteral = EmbeddingUtilities.FormatPgVector(vector);
        var param = command.CreateParameter();
        param.ParameterName = name;
        param.Value = vectorLiteral;
        command.Parameters.Add(param);
    }

    /// <inheritdoc />
    public async Task<List<SemanticProductMatch>> SearchByMultiVectorAsync(
        MultiVectorQuery query,
        SearchFilters? filters,
        float matchThreshold,
        float retrievalThreshold,
        int matchCount,
        (float Description, float Specs, float Enrichment) weights,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        _logger.LogDebug(
            "Multi-vector search: retrieval threshold={RetrievalThreshold}, final threshold={Threshold}, weights=({Dw},{Sw},{Ew})",
            retrievalThreshold, matchThreshold, weights.Description, weights.Specs, weights.Enrichment);

        // Phase 1: Retrieve candidates using description embedding (HNSW-accelerated)
        //          with a widened threshold. Return the raw embedding vectors for phase 2.
        var candidates = new List<(SemanticProductMatch Match, float[] DescEmb, float[] SpecsEmb, float[] EnrichEmb)>();

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

            var whereConditions = new List<string>
            {
                "p.embedding_description IS NOT NULL",
                "p.is_active = true",
                "1 - (p.embedding_description <=> @q_desc::vector) > @retrieval_threshold"
            };

            if (!string.IsNullOrWhiteSpace(filters?.FamilyLabel))
                whereConditions.Add("p.family_label = @family_label");
            if (!string.IsNullOrWhiteSpace(filters?.VendorName))
                whereConditions.Add("v.name = @vendor_name");

            var specFilterParams = new List<(string ParamName, string Value)>();
            if (filters?.SpecificationContainmentFilters != null)
            {
                for (var i = 0; i < filters.SpecificationContainmentFilters.Count; i++)
                {
                    var paramName = $"spec_filter_{i}";
                    whereConditions.Add($"pk.specifications @> @{paramName}::jsonb");
                    specFilterParams.Add((paramName, filters.SpecificationContainmentFilters[i]));
                }
            }

            // Fetch wider set of candidates (2x matchCount) so we have room after re-scoring
            var candidateLimit = matchCount * 2;

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
                    1 - (p.embedding_description <=> @q_desc::vector) AS desc_similarity,
                    p.embedding_description::text,
                    p.embedding_specs::text,
                    p.embedding_enrichment::text
                FROM public.products p
                JOIN public.vendors v ON p.vendor_id = v.vendor_id
                LEFT JOIN public.product_knowledge pk ON p.product_id = pk.product_id
                WHERE {string.Join(" AND ", whereConditions)}
                ORDER BY p.embedding_description <=> @q_desc::vector
                LIMIT @candidate_limit";

            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            AddVectorParameter(command, "q_desc", query.DescriptionEmbedding);

            var threshParam = command.CreateParameter();
            threshParam.ParameterName = "retrieval_threshold";
            threshParam.Value = retrievalThreshold;
            command.Parameters.Add(threshParam);

            var limitParam = command.CreateParameter();
            limitParam.ParameterName = "candidate_limit";
            limitParam.Value = candidateLimit;
            command.Parameters.Add(limitParam);

            if (!string.IsNullOrWhiteSpace(filters?.FamilyLabel))
            {
                var p = command.CreateParameter();
                p.ParameterName = "family_label";
                p.Value = filters.FamilyLabel;
                command.Parameters.Add(p);
            }
            if (!string.IsNullOrWhiteSpace(filters?.VendorName))
            {
                var p = command.CreateParameter();
                p.ParameterName = "vendor_name";
                p.Value = filters.VendorName;
                command.Parameters.Add(p);
            }
            foreach (var (paramName, value) in specFilterParams)
            {
                var p = command.CreateParameter();
                p.ParameterName = paramName;
                p.Value = value;
                command.Parameters.Add(p);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var match = new SemanticProductMatch
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
                    Similarity = reader.GetFloat(9) // description-only similarity for now
                };

                // Parse stored vectors from text representation
                var descEmbText = reader.IsDBNull(10) ? null : reader.GetString(10);
                var specsEmbText = reader.IsDBNull(11) ? null : reader.GetString(11);
                var enrichEmbText = reader.IsDBNull(12) ? null : reader.GetString(12);

                var descEmb = ParsePgVector(descEmbText);
                var specsEmb = ParsePgVector(specsEmbText);
                var enrichEmb = ParsePgVector(enrichEmbText);

                if (descEmb != null && specsEmb != null && enrichEmb != null)
                {
                    candidates.Add((match, descEmb, specsEmb, enrichEmb));
                }
            }

            _logger.LogDebug("Phase 1 retrieved {Count} candidates with description threshold {Threshold}",
                candidates.Count, retrievalThreshold);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Multi-vector semantic product search failed");
            throw;
        }

        // Phase 2: Compute weighted similarity in C# and filter against actual threshold
        var results = new List<SemanticProductMatch>();

        foreach (var (match, descEmb, specsEmb, enrichEmb) in candidates)
        {
            var descSim = EmbeddingUtilities.CosineSimilarity(query.DescriptionEmbedding, descEmb);
            var specsSim = EmbeddingUtilities.CosineSimilarity(query.SpecsEmbedding, specsEmb);
            var enrichSim = EmbeddingUtilities.CosineSimilarity(query.EnrichmentEmbedding, enrichEmb);

            var weightedScore = (weights.Description * descSim) +
                                (weights.Specs * specsSim) +
                                (weights.Enrichment * enrichSim);

            if (weightedScore >= matchThreshold)
            {
                results.Add(match with
                {
                    Similarity = weightedScore,
                    DescriptionSimilarity = descSim,
                    SpecsSimilarity = specsSim,
                    EnrichmentSimilarity = enrichSim
                });
            }
        }

        // Sort by weighted score descending, limit to matchCount
        results.Sort((a, b) => b.Similarity.CompareTo(a.Similarity));
        if (results.Count > matchCount)
            results = results.GetRange(0, matchCount);

        _logger.LogInformation(
            "Multi-vector search returned {Count} results above threshold {Threshold} (from {Candidates} candidates)",
            results.Count, matchThreshold, candidates.Count);

        return results;
    }

    /// <summary>
    /// Parse a PostgreSQL vector text representation (e.g., "[0.1,0.2,0.3]") back to float[].
    /// Returns null if the input is null or unparseable.
    /// </summary>
    private static float[]? ParsePgVector(string? vectorText)
    {
        if (string.IsNullOrWhiteSpace(vectorText))
            return null;

        var trimmed = vectorText.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            trimmed = trimmed[1..^1];

        var parts = trimmed.Split(',');
        var result = new float[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result[i]))
                return null;
        }

        return result;
    }
}
