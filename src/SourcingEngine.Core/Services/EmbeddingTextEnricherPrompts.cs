namespace SourcingEngine.Core.Services;

/// <summary>
/// Prompt templates for the <see cref="IEmbeddingTextEnricher"/> LLM calls.
/// Produces fluent natural-language text for embedding [DESCRIPTION] and [PRODUCTENRICHMENT] sections.
/// </summary>
public static class EmbeddingTextEnricherPrompts
{
    /// <summary>
    /// System prompt shared by both product and BOM item enrichment calls.
    /// </summary>
    public const string SystemPrompt = @"You are a construction materials technical writer.
Your job is to produce concise, fluent text that will be used as input for a vector embedding model.
The text should be rich in domain-relevant terminology, avoiding filler words.
Return ONLY valid JSON with exactly three keys: ""description"", ""technical_specs"", and ""enrichment"".
- ""description"" and ""enrichment"" are strings.
- ""technical_specs"" is a JSON array of objects, each with ""name"" (string), ""value"" (number, boolean, or string), and ""uom"" (string or null).
Do NOT include markdown, explanations, or anything outside the JSON object.";

    /// <summary>
    /// User prompt template for enriching a PRODUCT's embedding text.
    /// Placeholders: {model_name}, {vendor_name}, {family_label}, {specs}, {description},
    /// {use_cases}, {ideal_applications}, {not_recommended_for}, {certifications}, {finishes}, {key_features}
    /// </summary>
    public const string ProductUserPromptTemplate = @"Given this construction product data, write:
1) ""description"": A single concise sentence (max 40 words) describing the product. Include the model name, material type, and primary dimensions/UOM. Do not repeat the model name more than once.
2) ""technical_specs"": A JSON array of spec objects. Each object has: ""name"" (readable string, replace underscores with spaces, strip unit suffixes like _mm/_in), ""value"" (number, boolean, or string — use the raw value, e.g. 290 not ""290""), ""uom"" (unit string like ""mm"", ""in"", ""ft"", or null for booleans/non-dimensional). Extract ALL specs from the raw data. Examples: {""name"":""width"",""value"":290,""uom"":""mm""}, {""name"":""ssg available"",""value"":true,""uom"":null}, {""name"":""fabrication method"",""value"":""stick"",""uom"":null}. For array values (e.g. width options [100,140]), emit one object per value. Return empty array [] if no specs exist.
3) ""enrichment"": A concise paragraph (max 80 words) covering vendor context, material family, ideal use cases, when NOT to use it, available finishes, and key features. Write in natural construction-industry language.

PRODUCT DATA:
- Model: {model_name}
- Vendor: {vendor_name}
- Material Family: {family_label}
- Material: {material}
- Raw Specifications JSON: {specs}
- Description: {description}
- Use Cases: {use_cases}
- Ideal Applications: {ideal_applications}
- Not Recommended For: {not_recommended_for}
- Certifications: {certifications}
- Finishes: {finishes}
- Key Features: {key_features}

Return ONLY valid JSON:";

    /// <summary>
    /// System prompt for BOM LINE ITEM enrichment calls.
    /// Expects JSON with only two keys: <c>description</c> (string) and <c>enrichment</c> (string).
    /// Technical specs and certifications are taken directly from the BOM item — no LLM involvement.
    /// </summary>
    public const string BomItemSystemPrompt = @"You are a construction materials technical writer.
Your job is to produce concise, fluent text that will be used as input for a vector embedding model.
The text should be rich in domain-relevant terminology, avoiding filler words.
Return ONLY valid JSON with exactly two keys: ""description"" and ""enrichment"".
Both are strings.
Do NOT include markdown, explanations, or anything outside the JSON object.";

    /// <summary>
    /// User prompt template for enriching a BOM LINE ITEM's embedding text.
    /// The LLM only produces <c>description</c> and <c>enrichment</c>.
    /// Technical specs and certifications are sourced directly from the BOM item.
    /// Placeholders: {bom_item}, {bom_description}, {search_query}, {category},
    /// {technical_specs}, {certifications}, {notes}, {additional_data}, {family}, {attributes}
    /// </summary>
    public const string BomItemUserPromptTemplate = @"Given this BOM (Bill of Materials) line item, write:
1) ""description"": A single concise sentence (max 40 words). Include the item name, material type, and the main unit of measure with its value. Incorporate the expanded search terms naturally.
2) ""enrichment"": A concise paragraph (max 60 words) merging additional context (notes, origin, brand, grade, finish, category) into natural construction-industry language. If no additional context exists, return an empty string.

BOM ITEM DATA:
- Item Name: {bom_item}
- Description: {bom_description}
- Material: {material}
- Expanded Search Terms: {search_query}
- Category: {category}
- Technical Specs: {technical_specs}
- Certifications: {certifications}
- Notes: {notes}
- Additional Data: {additional_data}
- Material Family: {family}
- Attributes: {attributes}

Return ONLY valid JSON:";
}
