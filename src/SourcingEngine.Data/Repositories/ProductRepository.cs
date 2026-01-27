using Microsoft.Extensions.Logging;
using SourcingEngine.Core.Models;
using SourcingEngine.Core.Repositories;

namespace SourcingEngine.Data.Repositories;

/// <summary>
/// Product repository implementation
/// </summary>
public class ProductRepository : IProductRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<ProductRepository> _logger;

    public ProductRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<ProductRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<List<Product>> FindProductsAsync(
        string? familyLabel,
        string? csiCode,
        IEnumerable<string>? sizePatterns,
        IEnumerable<string>? keywords,
        CancellationToken cancellationToken = default)
    {
        var conditions = new List<string> { "p.is_active = true" };
        var parameters = new Dictionary<string, object>();
        var paramIndex = 0;

        // Family label filter
        if (!string.IsNullOrEmpty(familyLabel))
        {
            conditions.Add($"p.family_label = @p{paramIndex}");
            parameters[$"p{paramIndex++}"] = familyLabel;
        }

        // CSI code filter
        if (!string.IsNullOrEmpty(csiCode))
        {
            conditions.Add($"p.csi_section_code = @p{paramIndex}");
            parameters[$"p{paramIndex++}"] = csiCode;
        }

        // Size patterns filter (OR conditions)
        var sizeList = sizePatterns?.ToList() ?? [];
        if (sizeList.Count > 0)
        {
            var sizeConditions = sizeList
                .Select(_ => $"p.model_name ILIKE @p{paramIndex++}")
                .ToList();
            conditions.Add($"({string.Join(" OR ", sizeConditions)})");
            
            foreach (var size in sizeList)
            {
                parameters[$"p{paramIndex - sizeList.Count + sizeList.IndexOf(size)}"] = $"%{size}%";
            }
        }

        // Keywords filter (OR conditions on model_name)
        var keywordList = keywords?.ToList() ?? [];
        if (keywordList.Count > 0 && sizeList.Count == 0)
        {
            var keywordConditions = keywordList
                .Select(_ => $"p.model_name ILIKE @p{paramIndex++}")
                .ToList();
            conditions.Add($"({string.Join(" OR ", keywordConditions)})");
            
            foreach (var keyword in keywordList)
            {
                parameters[$"p{paramIndex - keywordList.Count + keywordList.IndexOf(keyword)}"] = $"%{keyword}%";
            }
        }

        var sql = $@"
            SELECT p.product_id, p.vendor_id, v.name as vendor_name, p.model_name, 
                   p.family_label, p.csi_section_code, p.is_active, p.base_price, 
                   p.average_lead_time_days
            FROM public.products p
            JOIN public.vendors v ON p.vendor_id = v.vendor_id
            WHERE {string.Join(" AND ", conditions)}
            ORDER BY v.name, p.model_name
            LIMIT 100";

        var products = new List<Product>();

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            foreach (var kvp in parameters)
            {
                var param = command.CreateParameter();
                param.ParameterName = kvp.Key;
                param.Value = kvp.Value;
                command.Parameters.Add(param);
            }

            _logger.LogDebug("Executing product search: {Sql}", sql);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                products.Add(new Product
                {
                    ProductId = reader.GetGuid(0),
                    VendorId = reader.GetInt32(1),
                    VendorName = reader.GetString(2),
                    ModelName = reader.GetString(3),
                    FamilyLabel = reader.IsDBNull(4) ? null : reader.GetString(4),
                    CsiSectionCode = reader.IsDBNull(5) ? null : reader.GetString(5),
                    IsActive = reader.GetBoolean(6),
                    BasePrice = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                    AverageLeadTimeDays = reader.IsDBNull(8) ? null : reader.GetInt32(8)
                });
            }

            _logger.LogInformation("Found {Count} products for family={Family}, csi={Csi}", 
                products.Count, familyLabel, csiCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find products");
            throw;
        }

        return products;
    }
}
