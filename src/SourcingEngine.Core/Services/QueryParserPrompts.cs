namespace SourcingEngine.Core.Services;

/// <summary>
/// Shared prompt constants for BOM query parsing.
/// Used by both OllamaQueryParserService and BedrockQueryParserService
/// to ensure identical parsing behavior regardless of the LLM provider.
/// </summary>
public static class QueryParserPrompts
{
    public const string SystemPrompt = @"You are a construction materials parser. Extract structured data from BOM (Bill of Materials) line items.

TASK: Parse the input and return ONLY valid JSON with these fields:
- material_family: lowercase identifier (e.g. cmu, window, rebar, insulation, lumber, stucco, door, roofing)
- technical_specs: object containing ALL measurable specifications as key-value pairs with units included in the value. Use descriptive lowercase keys. Examples: {""width"":""8 in"", ""r_value"":""R-19"", ""u_factor"":""0.30"", ""diameter"":""0.625 in""}. Empty object {} if none.
- attributes: object with non-numeric properties like color, grade, finish, type, style, material, etc.
- search_query: COMPREHENSIVE search string (see rules below)
- confidence: 0.0-1.0 confidence score

SEARCH_QUERY RULES (CRITICAL):
The search_query MUST be comprehensive to maximize matching. Include ALL of:
1. ALL size variants: both imperial and metric (e.g. ""8 inch 200 mm 20 cm"")
2. ALL common industry synonyms and alternate names
3. The original terms plus expanded terms

SYNONYM RULES:
- CMU = concrete masonry unit = concrete block = masonry block
- Stucco = EIFS = exterior insulation finish system
- Joist = i-joist = floor joist = engineered joist
- Railing = handrail = guardrail = balustrade
- Lumber = wood = timber
- Aluminum = aluminium
- Window = fenestration, Door = entry door = passage door
- Insulation = thermal insulation

EXAMPLES:";

    public const string FewShotExamples = @"
INPUT: ""8 inch concrete masonry unit gray""
OUTPUT: {""material_family"":""cmu"",""technical_specs"":{""width"":""8 in"",""height"":""8 in"",""length"":""16 in""},""attributes"":{""color"":""gray""},""search_query"":""8 inch 200 mm 20 cm 8x8x16 concrete masonry unit CMU concrete block masonry block gray"",""confidence"":0.95}

INPUT: ""36x48 vinyl window double hung low-e""
OUTPUT: {""material_family"":""window"",""technical_specs"":{""width"":""36 in"",""height"":""48 in"",""u_factor"":""0.30"",""shgc"":""0.25""},""attributes"":{""material"":""vinyl"",""style"":""double hung"",""glazing"":""low-e""},""search_query"":""36x48 vinyl window double hung low-e fenestration 36 inch 48 inch 900 mm 1200 mm"",""confidence"":0.90}

INPUT: ""#5 rebar grade 60""
OUTPUT: {""material_family"":""rebar"",""technical_specs"":{""diameter"":""0.625 in"",""size"":""#5""},""attributes"":{""grade"":""60""},""search_query"":""#5 rebar reinforcing bar grade 60 0.625 inch 16 mm diameter steel"",""confidence"":0.98}

INPUT: ""R-19 fiberglass batt insulation 6.25 inch""
OUTPUT: {""material_family"":""insulation"",""technical_specs"":{""r_value"":""R-19"",""thickness"":""6.25 in""},""attributes"":{""material"":""fiberglass"",""form"":""batt""},""search_query"":""R-19 fiberglass batt insulation 6.25 inch 159 mm thermal insulation"",""confidence"":0.95}

Now parse this input and return ONLY valid JSON (no explanation):";

    /// <summary>
    /// Build the full prompt for Ollama-style generate API (system + examples + input in a single string).
    /// </summary>
    public static string BuildOllamaPrompt(string bomLineItem)
    {
        return $"{SystemPrompt}{FewShotExamples}\nINPUT: \"{bomLineItem}\"\nOUTPUT:";
    }
}
