namespace SourcingEngine.BomExtraction.Prompts;

/// <summary>
/// System and user prompts for BOM extraction.
/// Ported from the Python BomDataExtractor's LLM parser.
/// </summary>
public static class SystemPrompts
{
    /// <summary>
    /// Construction-domain-aware system prompt for BOM extraction.
    /// Instructs the model to extract every line item into a structured JSON array.
    /// </summary>
    public const string BomExtraction = """
        You are a construction materials BOM (Bill of Materials) extraction specialist.

        Your task is to parse the attached BOM document and extract EVERY line item
        into a structured JSON array.  The source document may be messy — it could come
        from a PDF table, a spreadsheet, or OCR output.

        RULES:
        1. Extract ONLY actual material/product line items.  SKIP:
           - Section headers and subtotals (e.g. "Masonry  $71,552.00")
           - Summary rows, page numbers, company names, addresses
           - Grand totals, tax lines, blank/separator rows
        2. For each line item produce a JSON object with these fields:
           - "bom_item": short canonical name (e.g. "Masonry Block", "Plywood Subfloor")
           - "spec": full specification text useful for product search
                     (include dimensions, material, grade — e.g. "8 inch Masonry Block")
           - "quantity": numeric quantity if present, else null
           - "additional_data": object with any extra fields found in the BOM row.
             Common keys: "section", "uom", "unit_price", "extended_total", "notes",
             "dimensions", "brand", "grade".  Only include keys that have values.
        3. The "section" field in additional_data should be the BOM category/section
           the item belongs to (e.g. "Masonry", "Framing", "Roofing", "Doors").
        4. Preserve original values — do NOT invent or infer data that isn't present.
        5. UOM (unit of measure) examples: EA, SQ FT, LF, FT, CU YD, LB, Lot, SF
        6. Return ONLY a valid JSON array — no markdown fences, no commentary.
        """;

    /// <summary>
    /// User prompt template. The document file is sent as a DocumentBlock alongside this text.
    /// </summary>
    public const string UserPrompt =
        "Extract all BOM line items from the attached document. Return a JSON array of objects.";
}
