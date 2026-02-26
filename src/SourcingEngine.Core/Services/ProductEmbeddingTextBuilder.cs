using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Builds rich text representations of products for embedding generation.
/// Combines product name, specifications, and enriched data into a format
/// optimized for semantic similarity search.
/// </summary>
public interface IProductEmbeddingTextBuilder
{
    /// <summary>
    /// Build embedding text for a product
    /// </summary>
    /// <param name="product">Product data from public.products</param>
    /// <returns>Formatted text optimized for embedding</returns>
    string BuildEmbeddingText(ProductEmbeddingInput product);
}

/// <summary>
/// Input data for building product embedding text
/// </summary>
public class ProductEmbeddingInput
{
    /// <summary>
    /// Product UUID from public.products.product_id
    /// </summary>
    public Guid ProductId { get; set; }
    
    /// <summary>
    /// Model name from public.products.model_name
    /// </summary>
    public string? ModelName { get; set; }
    
    /// <summary>
    /// Description from product_knowledge.description or vendor enriched
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Material family label (e.g., "cmu_blocks", "floor_joists")
    /// </summary>
    public string? FamilyLabel { get; set; }
    
    /// <summary>
    /// Specifications JSON from product_knowledge.specifications
    /// </summary>
    public string? SpecificationsJson { get; set; }
    
    /// <summary>
    /// Vendor/brand name from vendors.name
    /// </summary>
    public string? VendorName { get; set; }
    
    /// <summary>
    /// Use cases from product_knowledge.use_cases
    /// </summary>
    public string? UseCases { get; set; }
    
    /// <summary>
    /// Ideal applications from product_knowledge.ideal_applications
    /// </summary>
    public string? IdealApplications { get; set; }
    
    /// <summary>
    /// When NOT to use this product from product_knowledge.not_recommended_for
    /// </summary>
    public string? NotRecommendedFor { get; set; }
    
    /// <summary>
    /// Key features JSON (from vendor enriched data)
    /// </summary>
    public string? KeyFeaturesJson { get; set; }
}

/// <summary>
/// Builds structured text for product embeddings.
/// </summary>
/// <remarks>
/// Output format follows semantic sections for optimal embedding:
/// [PRODUCT] {name} {sku}
/// [TYPE] {product_type}
/// [VENDOR] {vendor_name}
/// [FAMILY] {family_label}
/// [TECHNICALSPECS] {dimensions and specifications}
/// [DESCRIPTION] {description}
/// [USE] {use_when} {best_for}
/// [AVOID] {dont_use_when}
/// </remarks>
public class ProductEmbeddingTextBuilder : IProductEmbeddingTextBuilder
{
    private readonly ILogger<ProductEmbeddingTextBuilder> _logger;

    // Simple regex for extracting dimension values from text (e.g., "8 inch", "200mm")
    private static readonly Regex DimensionPattern = new(
        @"(\d+(?:\.\d+)?)\s*(inch|in|""|cm|mm|feet|ft|m)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ProductEmbeddingTextBuilder(ILogger<ProductEmbeddingTextBuilder> logger)
    {
        _logger = logger;
    }

    public string BuildEmbeddingText(ProductEmbeddingInput product)
    {
        var sb = new StringBuilder();

        // [PRODUCT] section - model name
        AppendSection(sb, "PRODUCT", product.ModelName);

        // [VENDOR] section - brand/manufacturer
        AppendSection(sb, "VENDOR", product.VendorName);

        // [FAMILY] section - material family
        AppendSection(sb, "FAMILY", FormatFamilyLabel(product.FamilyLabel));

        // [TECHNICALSPECS] section - dimensions and specifications
        var specs = BuildTechnicalSpecs(product);
        AppendSection(sb, "TECHNICALSPECS", specs);

        // [DESCRIPTION] section
        AppendSection(sb, "DESCRIPTION", CleanDescription(product.Description));

        // [USE] section - use cases and ideal applications
        var useText = JoinNonEmpty(" ", product.UseCases, product.IdealApplications);
        AppendSection(sb, "USE", useText);

        // [AVOID] section - when not to use
        AppendSection(sb, "AVOID", product.NotRecommendedFor);

        // [FEATURES] section - key features from enriched data
        var features = ExtractKeyFeatures(product.KeyFeaturesJson);
        AppendSection(sb, "FEATURES", features);

        return sb.ToString().Trim();
    }

    private string BuildTechnicalSpecs(ProductEmbeddingInput product)
    {
        var specs = new List<string>();

        // Parse specifications JSON — handle both array and object formats
        if (!string.IsNullOrWhiteSpace(product.SpecificationsJson))
        {
            specs.AddRange(ParseSpecificationsJson(product.SpecificationsJson, product.ProductId));
        }

        // Extract dimensions from description if not already in specs
        if (!string.IsNullOrWhiteSpace(product.Description) && specs.Count == 0)
        {
            var descDimensions = ExtractDimensionsFromText(product.Description);
            if (!string.IsNullOrWhiteSpace(descDimensions))
            {
                specs.Add(descDimensions);
            }
        }

        // Also try to extract from model name
        if (!string.IsNullOrWhiteSpace(product.ModelName) && specs.Count == 0)
        {
            var modelDimensions = ExtractDimensionsFromText(product.ModelName);
            if (!string.IsNullOrWhiteSpace(modelDimensions))
            {
                specs.Add(modelDimensions);
            }
        }

        return string.Join(" | ", specs);
    }

    /// <summary>
    /// Parse specifications JSON, handling both the legacy array format
    /// <c>[{name, value, unit}]</c> and the actual DB object format
    /// <c>{key: value}</c> used across all product families.
    /// Object format values can be: scalar numbers, strings, arrays, booleans.
    /// Dimensional values are emitted with multi-unit conversion via <see cref="DimensionUnitConverter"/>.
    /// </summary>
    internal List<string> ParseSpecificationsJson(string json, Guid productId)
    {
        var specs = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                // Legacy format: [{name, value, unit}, ...]
                var specArray = JsonSerializer.Deserialize<List<SpecificationItem>>(
                    json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (specArray != null)
                {
                    foreach (var spec in specArray)
                    {
                        var specText = FormatSpecification(spec);
                        if (!string.IsNullOrWhiteSpace(specText))
                            specs.Add(specText);
                    }
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                // Actual DB format: flat object with typed values
                // Handles: scalars, arrays, booleans — with multi-unit conversion for dimensional keys
                var dict = new Dictionary<string, JsonElement>();
                foreach (var prop in root.EnumerateObject())
                {
                    dict[prop.Name] = prop.Value.Clone();
                }
                specs.AddRange(DimensionUnitConverter.FormatSpecsFromJsonObject(dict));
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse specifications JSON for product {ProductId}", productId);
        }

        return specs;
    }

    private static string FormatSpecification(SpecificationItem spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Value))
            return string.Empty;

        var name = spec.Name?.ToLowerInvariant() ?? "unknown";
        var value = spec.Value.Trim();
        var unit = spec.Unit;

        // Format with unit if present
        if (!string.IsNullOrWhiteSpace(unit))
        {
            return $"{name}: {value} {unit}";
        }

        return $"{name}: {value}";
    }

    private static string ExtractDimensionsFromText(string text)
    {
        var match = DimensionPattern.Match(text);
        if (match.Success)
        {
            return $"size: {match.Value}";
        }
        return string.Empty;
    }

    private static string FormatFamilyLabel(string? familyLabel)
    {
        if (string.IsNullOrWhiteSpace(familyLabel))
            return string.Empty;

        // Convert underscore_case to readable format and include original
        var readable = familyLabel.Replace("_", " ");
        return $"{readable} ({familyLabel})";
    }

    private static string CleanDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return string.Empty;

        return description.Trim();
    }

    private static string ExtractKeyFeatures(string? keyFeaturesJson)
    {
        if (string.IsNullOrWhiteSpace(keyFeaturesJson))
            return string.Empty;

        try
        {
            // Try parsing as array of strings
            var features = JsonSerializer.Deserialize<List<string>>(keyFeaturesJson);
            if (features != null && features.Count > 0)
            {
                return string.Join(", ", features.Where(f => !string.IsNullOrWhiteSpace(f)));
            }
        }
        catch
        {
            // Try parsing as object with values
            try
            {
                var featuresObj = JsonSerializer.Deserialize<Dictionary<string, string>>(keyFeaturesJson);
                if (featuresObj != null && featuresObj.Count > 0)
                {
                    return string.Join(", ", featuresObj.Values.Where(v => !string.IsNullOrWhiteSpace(v)));
                }
            }
            catch
            {
                // Return raw string as fallback
                return keyFeaturesJson;
            }
        }

        return string.Empty;
    }

    private static void AppendSection(StringBuilder sb, string sectionName, string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        sb.Append('[').Append(sectionName).Append("] ");
        sb.Append(content.Trim());
        sb.Append(' ');
    }

    private static string JoinNonEmpty(string separator, params string?[] values)
    {
        return string.Join(separator, values.Where(v => !string.IsNullOrWhiteSpace(v)));
    }
}

/// <summary>
/// Specification item from products.specifications JSONB
/// </summary>
internal class SpecificationItem
{
    public string? Name { get; set; }
    public string? Value { get; set; }
    public string? Unit { get; set; }
    public string? ValueType { get; set; }
}
