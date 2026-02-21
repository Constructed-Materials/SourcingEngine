namespace SourcingEngine.Core.Services;

/// <summary>
/// Shared prompt constants for BOM query parsing.
/// Used by both OllamaQueryParserService and BedrockQueryParserService
/// to ensure identical parsing behavior regardless of the LLM provider.
/// </summary>
public static class QueryParserPrompts
{
    public const string SystemPrompt = @"You are a construction materials parser. Extract structured data from BOM (Bill of Materials) line items.

TASK: Parse the input and return JSON with these fields:
- material_family: lowercase identifier (cmu, floor_joist, stucco, rebar, lumber, etc.)
- width_inches: width dimension in inches (null if not specified)
- height_inches: height dimension in inches (null if not specified)  
- length_inches: length dimension in inches (null if not specified)
- thickness_inches: thickness in inches (null if not specified)
- diameter_inches: diameter in inches for round items (null if not specified)
- attributes: object with color, grade, finish, type, etc.
- search_query: COMPREHENSIVE search string (see rules below)
- confidence: 0.0-1.0 confidence score

SEARCH_QUERY RULES (CRITICAL):
The search_query MUST be as comprehensive as possible to maximize matching. Include ALL of:
1. ALL size variants with ROUNDED metric/imperial conversions
2. ALL common industry synonyms and alternate names for the material
3. The original terms plus expanded terms

DIMENSION CONVERSION TABLE (use ROUNDED practical values):
- 1 inch = 25 mm (rounded to nearest 5 or 10mm for common sizes)
- 1 inch = 2.5 cm (rounded)
- 1 ft = 0.3 m (rounded), 1 m = 3.3 ft (rounded)
- 1 sqft = 0.09 sqm (rounded), 1 sqm = 10.8 sqft (rounded)
- Standard CMU: 8x8x16 inches = 200x200x400 mm = 20x20x40 cm
- Rebar: #3=0.375in=10mm, #4=0.5in=13mm, #5=0.625in=16mm, #6=0.75in=19mm, #8=1.0in=25mm

SYNONYM RULES:
- CMU = concrete masonry unit = concrete block = masonry block = masonry unit
- Stucco = EIFS = exterior insulation
- Joist = i-joist = floor joist = engineered joist
- Railing = handrail = guardrail = balustrade
- LVL = laminated veneer lumber, LSL = laminated strand lumber
- Lumber = wood = timber
- Aluminum = aluminium

EXAMPLES:";

    public const string FewShotExamples = @"
INPUT: ""8 inch concrete masonry unit gray""
OUTPUT: {""material_family"":""cmu"",""width_inches"":8,""height_inches"":8,""length_inches"":16,""thickness_inches"":null,""diameter_inches"":null,""attributes"":{""color"":""gray""},""search_query"":""8 inch 20 cm 200 mm 8x8x16 concrete masonry unit CMU concrete block masonry block masonry unit gray"",""confidence"":0.95}

INPUT: ""2x10 floor joist 12ft pressure treated""
OUTPUT: {""material_family"":""floor_joist"",""width_inches"":1.5,""height_inches"":9.25,""length_inches"":144,""thickness_inches"":null,""diameter_inches"":null,""attributes"":{""treatment"":""pressure treated"",""nominal"":""2x10""},""search_query"":""2x10 floor joist i-joist engineered joist 12 ft 4 m lumber wood pressure treated"",""confidence"":0.92}

INPUT: ""#5 rebar grade 60""
OUTPUT: {""material_family"":""rebar"",""width_inches"":null,""height_inches"":null,""length_inches"":null,""thickness_inches"":null,""diameter_inches"":0.625,""attributes"":{""grade"":""60"",""size"":""#5""},""search_query"":""#5 rebar reinforcing bar grade 60 0.625 inch 16 mm diameter steel"",""confidence"":0.98}

INPUT: ""200mm masonry block lightweight""
OUTPUT: {""material_family"":""cmu"",""width_inches"":7.87,""height_inches"":7.87,""length_inches"":15.75,""thickness_inches"":null,""diameter_inches"":null,""attributes"":{""weight"":""lightweight""},""search_query"":""200 mm 20 cm 8 inch concrete masonry unit CMU concrete block masonry block masonry unit lightweight"",""confidence"":0.88}

Now parse this input and return ONLY valid JSON (no explanation):";

    /// <summary>
    /// Build the full prompt for Ollama-style generate API (system + examples + input in a single string).
    /// </summary>
    public static string BuildOllamaPrompt(string bomLineItem)
    {
        return $"{SystemPrompt}{FewShotExamples}\nINPUT: \"{bomLineItem}\"\nOUTPUT:";
    }
}
