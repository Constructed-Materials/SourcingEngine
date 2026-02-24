using Microsoft.Extensions.Logging.Abstractions;
using SourcingEngine.BomExtraction.Parsing;

namespace SourcingEngine.BomExtraction.Tests.Unit;

public class JsonResponseParserTests
{
    private readonly JsonResponseParser _parser = new(NullLogger<JsonResponseParser>.Instance);

    // ---------------------------------------------------------------
    // Clean JSON array
    // ---------------------------------------------------------------

    [Fact]
    public void Parse_CleanJsonArray_ReturnsItems()
    {
        var json = """
            [
                {"bom_item": "Masonry Block", "spec": "8 inch CMU Block", "quantity": 1200, "additional_data": {"section": "Masonry", "uom": "EA"}},
                {"bom_item": "Rebar", "spec": "#5 Rebar 20ft", "quantity": 350, "additional_data": {"uom": "EA"}}
            ]
            """;

        var items = _parser.Parse(json);

        Assert.Equal(2, items.Count);
        Assert.Equal("Masonry Block", items[0].BomItem);
        Assert.Equal("8 inch CMU Block", items[0].Spec);
        Assert.Equal(1200, items[0].Quantity);
        Assert.Equal("Masonry", items[0].AdditionalData["section"]?.ToString());
        Assert.Equal("Rebar", items[1].BomItem);
    }

    // ---------------------------------------------------------------
    // Markdown fences
    // ---------------------------------------------------------------

    [Fact]
    public void Parse_JsonWrappedInMarkdownFences_ReturnsItems()
    {
        var json = """
            ```json
            [
                {"bom_item": "Concrete", "spec": "3000 PSI Ready Mix", "quantity": 45}
            ]
            ```
            """;

        var items = _parser.Parse(json);

        Assert.Single(items);
        Assert.Equal("Concrete", items[0].BomItem);
    }

    [Fact]
    public void Parse_MarkdownFencesNoLanguageTag_ReturnsItems()
    {
        var json = """
            ```
            [{"bom_item": "Lumber", "spec": "2x4x8 SPF", "quantity": 100}]
            ```
            """;

        var items = _parser.Parse(json);

        Assert.Single(items);
        Assert.Equal("Lumber", items[0].BomItem);
    }

    // ---------------------------------------------------------------
    // Wrapper object { "items": [...] }
    // ---------------------------------------------------------------

    [Fact]
    public void Parse_WrapperObjectWithItems_ReturnsItems()
    {
        var json = """
            {"items": [{"bom_item": "Plywood", "spec": "3/4 CDX 4x8", "quantity": 200}]}
            """;

        var items = _parser.Parse(json);

        Assert.Single(items);
        Assert.Equal("Plywood", items[0].BomItem);
    }

    // ---------------------------------------------------------------
    // Trailing commentary after JSON
    // ---------------------------------------------------------------

    [Fact]
    public void Parse_JsonWithTrailingCommentary_ReturnsItems()
    {
        var json = """
            [{"bom_item": "Mortar", "spec": "Type S Mortar", "quantity": 150}]

            Note: I extracted all items from the document.
            """;

        var items = _parser.Parse(json);

        Assert.Single(items);
        Assert.Equal("Mortar", items[0].BomItem);
    }

    // ---------------------------------------------------------------
    // Numeric sanitization
    // ---------------------------------------------------------------

    [Fact]
    public void SanitizeNumericLiterals_ThousandsSeparators_Removed()
    {
        var input = """[{"quantity": 50,000, "name": "test"}]""";
        var sanitized = JsonResponseParser.SanitizeNumericLiterals(input);

        Assert.Contains("50000", sanitized);
        Assert.DoesNotContain("50,000", sanitized);
    }

    [Fact]
    public void SanitizeNumericLiterals_ThousandsWithDecimals_Preserved()
    {
        var input = """[{"quantity": 1,234.56}]""";
        var sanitized = JsonResponseParser.SanitizeNumericLiterals(input);

        Assert.Contains("1234.56", sanitized);
    }

    [Fact]
    public void SanitizeNumericLiterals_TrailingDot_Removed()
    {
        var input = """[{"quantity": 50000.}]""";
        var sanitized = JsonResponseParser.SanitizeNumericLiterals(input);

        Assert.Contains("50000}", sanitized);
        Assert.DoesNotContain("50000.", sanitized);
    }

    // ---------------------------------------------------------------
    // camelCase / snake_case tolerance
    // ---------------------------------------------------------------

    [Fact]
    public void Parse_CamelCaseKeys_ReturnsItems()
    {
        var json = """
            [{"bomItem": "Stucco", "spec": "3 coat stucco system", "quantity": 500, "additionalData": {"uom": "SF"}}]
            """;

        var items = _parser.Parse(json);

        Assert.Single(items);
        Assert.Equal("Stucco", items[0].BomItem);
        Assert.Equal("SF", items[0].AdditionalData["uom"]?.ToString());
    }

    // ---------------------------------------------------------------
    // Null / missing quantity
    // ---------------------------------------------------------------

    [Fact]
    public void Parse_NullQuantity_ReturnsNullQuantity()
    {
        var json = """[{"bom_item": "Sealant", "spec": "Polyurethane sealant", "quantity": null}]""";

        var items = _parser.Parse(json);

        Assert.Single(items);
        Assert.Null(items[0].Quantity);
    }

    [Fact]
    public void Parse_MissingQuantity_ReturnsNullQuantity()
    {
        var json = """[{"bom_item": "Sealant", "spec": "Polyurethane sealant"}]""";

        var items = _parser.Parse(json);

        Assert.Single(items);
        Assert.Null(items[0].Quantity);
    }

    // ---------------------------------------------------------------
    // Validation — skip invalid items
    // ---------------------------------------------------------------

    [Fact]
    public void Parse_MissingBomItem_SkipsItem()
    {
        var json = """
            [
                {"bom_item": "Valid Item", "spec": "Valid spec"},
                {"bom_item": "", "spec": "Missing bom_item"},
                {"spec": "Also missing bom_item"}
            ]
            """;

        var items = _parser.Parse(json);

        Assert.Single(items);
        Assert.Equal("Valid Item", items[0].BomItem);
    }

    [Fact]
    public void Parse_MissingSpec_SkipsItem()
    {
        var json = """
            [
                {"bom_item": "Valid Item", "spec": "Has spec"},
                {"bom_item": "No Spec Item", "spec": ""}
            ]
            """;

        var items = _parser.Parse(json);

        Assert.Single(items);
    }

    // ---------------------------------------------------------------
    // Empty / invalid responses
    // ---------------------------------------------------------------

    [Fact]
    public void Parse_EmptyResponse_ThrowsBomParsingException()
    {
        Assert.Throws<BomParsingException>(() => _parser.Parse(""));
    }

    [Fact]
    public void Parse_WhitespaceResponse_ThrowsBomParsingException()
    {
        Assert.Throws<BomParsingException>(() => _parser.Parse("   "));
    }

    [Fact]
    public void Parse_InvalidJson_ThrowsBomParsingException()
    {
        Assert.Throws<BomParsingException>(() => _parser.Parse("this is not json at all"));
    }

    [Fact]
    public void Parse_EmptyArray_ReturnsEmptyList()
    {
        var items = _parser.Parse("[]");
        Assert.Empty(items);
    }

    // ---------------------------------------------------------------
    // Additional data extraction
    // ---------------------------------------------------------------

    [Fact]
    public void Parse_AdditionalDataWithMultipleKeys_AllPreserved()
    {
        var json = """
            [{
                "bom_item": "CMU Block",
                "spec": "8 inch CMU Block",
                "quantity": 1200,
                "additional_data": {
                    "section": "Masonry",
                    "uom": "EA",
                    "unit_price": 3.50,
                    "extended_total": 4200.00,
                    "notes": "Load-bearing walls"
                }
            }]
            """;

        var items = _parser.Parse(json);

        Assert.Single(items);
        var data = items[0].AdditionalData;
        Assert.Equal(5, data.Count);
        Assert.Equal("Masonry", data["section"]?.ToString());
        Assert.Equal(3.5, Convert.ToDouble(data["unit_price"]));
        Assert.Equal("Load-bearing walls", data["notes"]?.ToString());
    }

    [Fact]
    public void Parse_MissingAdditionalData_ReturnsEmptyDict()
    {
        var json = """[{"bom_item": "Nails", "spec": "16d Common Nails"}]""";

        var items = _parser.Parse(json);

        Assert.Single(items);
        Assert.Empty(items[0].AdditionalData);
    }

    // ---------------------------------------------------------------
    // Quantity as string
    // ---------------------------------------------------------------

    [Fact]
    public void Parse_QuantityAsString_ParsedCorrectly()
    {
        var json = """[{"bom_item": "Lumber", "spec": "2x4 SPF", "quantity": "800"}]""";

        var items = _parser.Parse(json);

        Assert.Single(items);
        Assert.Equal(800, items[0].Quantity);
    }
}
