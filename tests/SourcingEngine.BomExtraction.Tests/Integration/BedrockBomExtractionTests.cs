using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SourcingEngine.BomExtraction.Configuration;
using SourcingEngine.BomExtraction.Parsing;
using SourcingEngine.BomExtraction.Services;

namespace SourcingEngine.BomExtraction.Tests.Integration;

/// <summary>
/// Integration tests that call the real Bedrock API with sample BOM files.
/// Requires valid AWS credentials.
///
/// Run with:  dotnet test --filter "Category=Integration"
/// Skip with: dotnet test --filter "Category!=Integration"
/// </summary>
[Trait("Category", "Integration")]
public class BedrockBomExtractionTests : IDisposable
{
    private readonly IBomExtractionService _service;
    private readonly IHost _host;

    /// <summary>TestData directory copied to the test output by the csproj wildcard rule.</summary>
    private static readonly string TestDataDir =
        Path.Combine(AppContext.BaseDirectory, "TestData");

    public BedrockBomExtractionTests()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(config =>
            {
                config.AddJsonFile("appsettings.json", optional: true);
                config.AddEnvironmentVariables();
            })
            .ConfigureServices((context, services) =>
            {
                services.Configure<BomExtractionSettings>(
                    context.Configuration.GetSection(BomExtractionSettings.SectionName));
                services.AddSingleton<JsonResponseParser>();
                services.AddSingleton<IBomExtractionService, BomExtractionService>();
            })
            .ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddConsole();
            })
            .Build();

        _service = _host.Services.GetRequiredService<IBomExtractionService>();
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    /// <summary>Resolve a TestData file path; returns null when the file is missing.</summary>
    private static string? TestDataFile(string fileName)
    {
        var path = Path.Combine(TestDataDir, fileName);
        return File.Exists(path) ? path : null;
    }

    // ================================================================
    //  CSV — estimate_ft_pierce_2story.csv
    //  ~60+ line items across Masonry, Framing, Roofing, Doors, etc.
    // ================================================================

    [Fact]
    public async Task Csv_ExtractsAtLeast20Items()
    {
        var path = TestDataFile("estimate_ft_pierce_2story.csv");
        Assert.NotNull(path);

        var result = await _service.ExtractAsync(path!);

        Assert.NotNull(result);
        Assert.True(result.ItemCount >= 20,
            $"Expected ≥20 items from CSV but got {result.ItemCount}. " +
            $"Warnings: {string.Join("; ", result.Warnings)}");
    }

    [Fact]
    public async Task Csv_AllItemsHaveRequiredFields()
    {
        var path = TestDataFile("estimate_ft_pierce_2story.csv");
        Assert.NotNull(path);

        var result = await _service.ExtractAsync(path!);

        foreach (var item in result.Items)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.BomItem),
                $"BomItem is empty. Spec: '{item.Spec}'");
            Assert.False(string.IsNullOrWhiteSpace(item.Spec),
                $"Spec is empty. BomItem: '{item.BomItem}'");
        }
    }

    [Fact]
    public async Task Csv_MostItemsHaveQuantities()
    {
        var path = TestDataFile("estimate_ft_pierce_2story.csv");
        Assert.NotNull(path);

        var result = await _service.ExtractAsync(path!);

        var withQuantity = result.Items.Count(i => i.Quantity.HasValue && i.Quantity > 0);
        Assert.True(withQuantity >= result.ItemCount * 0.7,
            $"Expected ≥70% of items to have quantities. " +
            $"Got {withQuantity}/{result.ItemCount}");
    }

    [Fact]
    public async Task Csv_ContainsExpectedSections()
    {
        var path = TestDataFile("estimate_ft_pierce_2story.csv");
        Assert.NotNull(path);

        var result = await _service.ExtractAsync(path!);

        // The CSV has Masonry, Framing, Roofing, Doors sections.
        var sections = result.Items
            .Where(i => i.AdditionalData.ContainsKey("section"))
            .Select(i => i.AdditionalData["section"]?.ToString()?.ToLowerInvariant() ?? "")
            .Distinct()
            .ToList();

        Assert.True(sections.Count >= 2,
            $"Expected items from ≥2 sections. Got: [{string.Join(", ", sections)}]");
    }

    [Fact]
    public async Task Csv_DoesNotExtractSectionHeadersAsItems()
    {
        var path = TestDataFile("estimate_ft_pierce_2story.csv");
        Assert.NotNull(path);

        var result = await _service.ExtractAsync(path!);

        // Section headers like "Masonry  $71,552.00" should NOT be line items
        var suspectHeaders = result.Items
            .Where(i =>
            {
                var name = i.BomItem.ToLowerInvariant();
                return name is "masonry" or "metals" or "framing"
                    or "insulation" or "roofing" or "exterior"
                    or "doors" or "windows";
            })
            .ToList();

        Assert.True(suspectHeaders.Count == 0,
            $"Section headers incorrectly extracted as items: " +
            $"[{string.Join(", ", suspectHeaders.Select(h => h.BomItem))}]");
    }

    [Fact]
    public async Task Csv_TokenUsageIsReported()
    {
        var path = TestDataFile("estimate_ft_pierce_2story.csv");
        Assert.NotNull(path);

        var result = await _service.ExtractAsync(path!);

        Assert.NotNull(result.InputTokens);
        Assert.True(result.InputTokens > 0, "InputTokens should be > 0");
        Assert.NotNull(result.OutputTokens);
        Assert.True(result.OutputTokens > 0, "OutputTokens should be > 0");
    }

    // ================================================================
    //  PDF — bom-1.pdf
    //  Tests the vision/document-chat path for PDF files.
    // ================================================================

    [Fact]
    public async Task Pdf_ExtractsAtLeast3Items()
    {
        var path = TestDataFile("bom-1.pdf");
        Assert.NotNull(path);

        var result = await _service.ExtractAsync(path!);

        Assert.NotNull(result);
        Assert.True(result.ItemCount >= 3,
            $"Expected ≥3 items from PDF but got {result.ItemCount}. " +
            $"Warnings: {string.Join("; ", result.Warnings)}");
    }

    [Fact]
    public async Task Pdf_AllItemsHaveRequiredFields()
    {
        var path = TestDataFile("bom-1.pdf");
        Assert.NotNull(path);

        var result = await _service.ExtractAsync(path!);

        foreach (var item in result.Items)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.BomItem),
                $"BomItem is empty. Spec: '{item.Spec}'");
            Assert.False(string.IsNullOrWhiteSpace(item.Spec),
                $"Spec is empty. BomItem: '{item.BomItem}'");
        }
    }

    [Fact]
    public async Task Pdf_SomeItemsHaveQuantities()
    {
        var path = TestDataFile("bom-1.pdf");
        Assert.NotNull(path);

        var result = await _service.ExtractAsync(path!);

        var withQuantity = result.Items.Count(i => i.Quantity.HasValue && i.Quantity > 0);
        Assert.True(withQuantity > 0,
            $"Expected some items with quantities from PDF. Total: {result.ItemCount}");
    }

    [Fact]
    public async Task Pdf_TokenUsageIsReported()
    {
        var path = TestDataFile("bom-1.pdf");
        Assert.NotNull(path);

        var result = await _service.ExtractAsync(path!);

        Assert.NotNull(result.InputTokens);
        Assert.True(result.InputTokens > 0);
        Assert.NotNull(result.OutputTokens);
        Assert.True(result.OutputTokens > 0);
    }

    // ================================================================
    //  XLSX — bom-2.xlsx
    //  Tests native spreadsheet document processing.
    // ================================================================

    [Fact]
    public async Task Xlsx_Extracts52Items()
    {
        var path = TestDataFile("bom-2.xlsx");
        Assert.NotNull(path);

        var result = await _service.ExtractAsync(path!);

        Assert.NotNull(result);
        Assert.True(result.ItemCount >= 52,
            $"Expected ≥52 items from XLSX but got {result.ItemCount}. " +
            $"Warnings: {string.Join("; ", result.Warnings)}");
    }

    [Fact]
    public async Task Xlsx_AllItemsHaveRequiredFields()
    {
        var path = TestDataFile("bom-2.xlsx");
        Assert.NotNull(path);

        var result = await _service.ExtractAsync(path!);

        foreach (var item in result.Items)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.BomItem),
                $"BomItem is empty. Spec: '{item.Spec}'");
            Assert.False(string.IsNullOrWhiteSpace(item.Spec),
                $"Spec is empty. BomItem: '{item.BomItem}'");
        }
    }

    [Fact]
    public async Task Xlsx_SomeItemsHaveQuantities()
    {
        var path = TestDataFile("bom-2.xlsx");
        Assert.NotNull(path);

        var result = await _service.ExtractAsync(path!);

        var withQuantity = result.Items.Count(i => i.Quantity.HasValue && i.Quantity > 0);
        Assert.True(withQuantity > 0,
            $"Expected some items with quantities from XLSX. Total: {result.ItemCount}");
    }

    [Fact]
    public async Task Xlsx_TokenUsageIsReported()
    {
        var path = TestDataFile("bom-2.xlsx");
        Assert.NotNull(path);

        var result = await _service.ExtractAsync(path!);

        Assert.NotNull(result.InputTokens);
        Assert.True(result.InputTokens > 0);
        Assert.NotNull(result.OutputTokens);
        Assert.True(result.OutputTokens > 0);
    }
}
