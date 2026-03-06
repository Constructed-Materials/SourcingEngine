using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SourcingEngine.Common.Models;
using SourcingEngine.Core.Models;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Builds rich text representations of products for embedding generation.
/// Uses the unified 5-section format:
/// [PRODUCT] [DESCRIPTION] [TECHNICALSPECS] [CERTIFICATIONS] [PRODUCTENRICHMENT]
/// </summary>
public interface IProductEmbeddingTextBuilder
{
    /// <summary>
    /// Build embedding text for a product using LLM enrichment.
    /// Always emits all 5 section labels in fixed order, even if empty.
    /// </summary>
    /// <param name="product">Product data from public.products + product_knowledge + certifications</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Formatted text optimized for embedding</returns>
    Task<string> BuildEmbeddingTextAsync(ProductEmbeddingInput product, CancellationToken cancellationToken = default);

    /// <summary>
    /// Build three separate embedding texts for multi-vector product embeddings.
    /// Vector A: [MATERIAL] [PRODUCT] [DESCRIPTION] [CERTIFICATIONS]
    /// Vector B: [TECHNICALSPECS]
    /// Vector C: [PRODUCTENRICHMENT]
    /// </summary>
    Task<MultiVectorEmbeddingText> BuildMultiVectorTextAsync(ProductEmbeddingInput product, CancellationToken cancellationToken = default);
}

/// <summary>
/// Input data for building product embedding text.
/// Aggregates data from public.products, product_knowledge, product_certifications, and certifications.
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
    /// Primary construction material derived from family_label
    /// (e.g. "concrete", "aluminum", "vinyl", "steel").
    /// Used for the [MATERIAL] section in multi-vector embeddings.
    /// </summary>
    public string? Material { get; set; }
    
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

    /// <summary>
    /// Certification names aggregated from product_certifications + certifications tables.
    /// Example: ["ASTM C90", "CSA A165.1", "CCMPA Member"]
    /// </summary>
    public List<string> Certifications { get; set; } = new();

    /// <summary>
    /// Finishes/colors JSON from product_finishes (optional).
    /// </summary>
    public string? FinishesJson { get; set; }
}

/// <summary>
/// Builds structured text for product embeddings using the unified 5-section format.
/// </summary>
/// <remarks>
/// Output format — all labels always present, fixed order:
/// [PRODUCT] {model_name}
/// [DESCRIPTION] {LLM-generated fluent description}
/// [TECHNICALSPECS] {normalized specs as "name: value uom | ..."}
/// [CERTIFICATIONS] {comma-separated certs or "[]"}
/// [PRODUCTENRICHMENT] {LLM-generated: vendor, family, use cases, avoid, finishes, features}
/// </remarks>
public class ProductEmbeddingTextBuilder : IProductEmbeddingTextBuilder
{
    private readonly IEmbeddingTextEnricher _enricher;
    private readonly ILogger<ProductEmbeddingTextBuilder> _logger;

    public ProductEmbeddingTextBuilder(
        IEmbeddingTextEnricher enricher,
        ILogger<ProductEmbeddingTextBuilder> logger)
    {
        _enricher = enricher;
        _logger = logger;
    }

    public async Task<string> BuildEmbeddingTextAsync(
        ProductEmbeddingInput product, CancellationToken cancellationToken = default)
    {
        // Get LLM-enriched description and enrichment text
        var enriched = await _enricher.EnrichProductTextAsync(product, cancellationToken);

        var sb = new StringBuilder();

        // [MATERIAL] — primary construction material (always present)
        AppendSection(sb, "MATERIAL", product.Material);

        // [PRODUCT] — model name (always present)
        AppendSection(sb, "PRODUCT", product.ModelName);

        // [DESCRIPTION] — LLM-generated fluent description
        AppendSection(sb, "DESCRIPTION", enriched.Description);

        // [TECHNICALSPECS] — JSON array of {name, value, uom} spec objects
        var specsJson = enriched.TechnicalSpecs.Count > 0
            ? JsonSerializer.Serialize(enriched.TechnicalSpecs)
            : null;
        AppendSection(sb, "TECHNICALSPECS", specsJson);

        // [CERTIFICATIONS] — from product_certifications table
        var certsText = product.Certifications.Count > 0
            ? string.Join(", ", product.Certifications)
            : null;
        AppendSection(sb, "CERTIFICATIONS", certsText);

        // [PRODUCTENRICHMENT] — LLM-generated: vendor, family, use cases, avoid, finishes, features
        AppendSection(sb, "PRODUCTENRICHMENT", enriched.Enrichment);

        return sb.ToString().Trim();
    }

    /// <inheritdoc />
    public async Task<MultiVectorEmbeddingText> BuildMultiVectorTextAsync(
        ProductEmbeddingInput product, CancellationToken cancellationToken = default)
    {
        var enriched = await _enricher.EnrichProductTextAsync(product, cancellationToken);

        // Vector A: [MATERIAL] [PRODUCT] [DESCRIPTION] [CERTIFICATIONS]
        var descSb = new StringBuilder();
        AppendSection(descSb, "MATERIAL", product.Material);
        AppendSection(descSb, "PRODUCT", product.ModelName);
        AppendSection(descSb, "DESCRIPTION", enriched.Description);
        var certsText = product.Certifications.Count > 0
            ? string.Join(", ", product.Certifications)
            : null;
        AppendSection(descSb, "CERTIFICATIONS", certsText);

        // Vector B: [TECHNICALSPECS]
        var specsSb = new StringBuilder();
        var specsJson = enriched.TechnicalSpecs.Count > 0
            ? JsonSerializer.Serialize(enriched.TechnicalSpecs)
            : null;
        AppendSection(specsSb, "TECHNICALSPECS", specsJson);

        // Vector C: [PRODUCTENRICHMENT]
        var enrichSb = new StringBuilder();
        AppendSection(enrichSb, "PRODUCTENRICHMENT", enriched.Enrichment);

        var descriptionText = descSb.ToString().Trim();
        var specsText = specsSb.ToString().Trim();
        var enrichmentText = enrichSb.ToString().Trim();

        // Full debug text: concat all 6 sections
        var fullDebug = $"{descriptionText} {specsText} {enrichmentText}";

        _logger.LogDebug(
            "Built multi-vector text for product {ModelName}: desc={DescLen}, specs={SpecsLen}, enrich={EnrichLen}",
            product.ModelName, descriptionText.Length, specsText.Length, enrichmentText.Length);

        return new MultiVectorEmbeddingText
        {
            DescriptionText = descriptionText,
            SpecsText = specsText,
            EnrichmentText = enrichmentText,
            FullDebugText = fullDebug
        };
    }

    /// <summary>
    /// Append a section to the embedding text. Always emits the label,
    /// using "[]" as placeholder when content is empty, to maintain
    /// structural alignment between product and query embeddings.
    /// </summary>
    private static void AppendSection(StringBuilder sb, string sectionName, string? content)
    {
        sb.Append('[').Append(sectionName).Append("] ");
        if (string.IsNullOrWhiteSpace(content))
            sb.Append("[]");
        else
            sb.Append(content.Trim());
        sb.Append(' ');
    }
}
