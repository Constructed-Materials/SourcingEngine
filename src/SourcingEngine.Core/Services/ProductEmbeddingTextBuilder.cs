using System.Text;
using System.Text.Json;
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
    private readonly ISizeCalculator _sizeCalculator;

    public ProductEmbeddingTextBuilder(
        ILogger<ProductEmbeddingTextBuilder> logger,
        ISizeCalculator sizeCalculator)
    {
        _logger = logger;
        _sizeCalculator = sizeCalculator;
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

        // Parse specifications JSON
        if (!string.IsNullOrWhiteSpace(product.SpecificationsJson))
        {
            try
            {
                var specArray = JsonSerializer.Deserialize<List<SpecificationItem>>(
                    product.SpecificationsJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (specArray != null)
                {
                    foreach (var spec in specArray)
                    {
                        var specText = FormatSpecification(spec);
                        if (!string.IsNullOrWhiteSpace(specText))
                        {
                            specs.Add(specText);
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse specifications JSON for product {ProductId}", product.ProductId);
            }
        }

        // Extract dimensions from description if not in specs
        if (!string.IsNullOrWhiteSpace(product.Description))
        {
            var descDimensions = ExtractDimensionsFromText(product.Description);
            if (!string.IsNullOrWhiteSpace(descDimensions) && !specs.Any(s => s.Contains("inch") || s.Contains("mm")))
            {
                specs.Add(descDimensions);
            }
        }

        // Also try to extract from model name (e.g., "BCI® 5000s 1.8 I-Joist")
        if (!string.IsNullOrWhiteSpace(product.ModelName))
        {
            var modelDimensions = ExtractDimensionsFromText(product.ModelName);
            if (!string.IsNullOrWhiteSpace(modelDimensions) && !specs.Any(s => s.Contains("inch") || s.Contains("mm")))
            {
                specs.Add(modelDimensions);
            }
        }

        return string.Join(" | ", specs);
    }

    private string FormatSpecification(SpecificationItem spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Value))
            return string.Empty;

        var name = spec.Name?.ToLowerInvariant() ?? "unknown";
        var value = spec.Value.Trim();
        var unit = spec.Unit;

        // Handle size/dimension specs specially
        if (name.Contains("size") || name.Contains("dimension"))
        {
            var normalized = NormalizeSizeValue(value);
            return $"size: {normalized}";
        }

        // Handle thickness
        if (name.Contains("thick"))
        {
            var normalized = NormalizeSizeValue($"{value} {unit}");
            return $"thickness: {normalized}";
        }

        // Format with unit if present
        if (!string.IsNullOrWhiteSpace(unit))
        {
            return $"{name}: {value} {unit}";
        }

        return $"{name}: {value}";
    }

    private string NormalizeSizeValue(string value)
    {
        // Use SizeCalculator to normalize dimensions
        // Extract numeric dimensions and convert to consistent format
        var size = _sizeCalculator.ExtractSize(value);
        if (size.HasValue)
        {
            // Convert to inches
            var inches = ConvertToInches(size.Value.Value, size.Value.Unit);
            var mm = inches * 25.4;
            return $"{inches:F1}\" ({mm:F0}mm)";
        }

        return value;
    }

    private string ExtractDimensionsFromText(string text)
    {
        var size = _sizeCalculator.ExtractSize(text);
        if (size.HasValue)
        {
            var inches = ConvertToInches(size.Value.Value, size.Value.Unit);
            return $"size: {inches:F1}\" ({inches * 25.4:F0}mm)";
        }
        return string.Empty;
    }

    private static double ConvertToInches(double value, string unit)
    {
        return unit.ToLowerInvariant() switch
        {
            "inch" or "in" or "\"" => value,
            "cm" => value / 2.54,
            "mm" => value / 25.4,
            _ => value
        };
    }

    private string FormatFamilyLabel(string? familyLabel)
    {
        if (string.IsNullOrWhiteSpace(familyLabel))
            return string.Empty;

        // Convert underscore_case to readable format and include original
        var readable = familyLabel.Replace("_", " ");
        return $"{readable} ({familyLabel})";
    }

    private string CleanDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return string.Empty;

        // Remove redundant spec data that will be in TECHNICALSPECS section
        // Keep the narrative/descriptive parts
        return description.Trim();
    }

    private string ExtractKeyFeatures(string? keyFeaturesJson)
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

    private void AppendSection(StringBuilder sb, string sectionName, string? content)
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
