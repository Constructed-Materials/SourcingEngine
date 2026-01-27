using Microsoft.Extensions.Logging;
using SourcingEngine.Core.Models;
using SourcingEngine.Core.Repositories;

namespace SourcingEngine.Data.Repositories;

/// <summary>
/// Material family repository implementation
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
                families.Add(new MaterialFamily
                {
                    FamilyLabel = reader.GetString(0),
                    FamilyName = reader.IsDBNull(1) ? null : reader.GetString(1),
                    CsiDivision = reader.IsDBNull(2) ? null : reader.GetString(2),
                    TypicalLeadTimeDays = reader.IsDBNull(3) ? null : reader.GetInt32(3)
                });
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
}
