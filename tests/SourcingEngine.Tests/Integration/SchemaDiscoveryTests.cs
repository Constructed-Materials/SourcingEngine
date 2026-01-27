using SourcingEngine.Data.Repositories;
using SourcingEngine.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace SourcingEngine.Tests.Integration;

/// <summary>
/// Integration tests for SchemaDiscoveryService - requires database
/// </summary>
[Collection("Database")]
[Trait("Category", "Integration")]
public class SchemaDiscoveryTests
{
    private readonly DatabaseFixture _fixture;
    private readonly ITestOutputHelper _output;

    public SchemaDiscoveryTests(DatabaseFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task GetVendorSchemas_ReturnsMinimum10Schemas()
    {
        // Arrange
        var service = _fixture.GetService<ISchemaDiscoveryService>();

        // Act
        var schemas = await service.GetVendorSchemasAsync();

        // Assert - minimum threshold, not exact count
        _output.WriteLine($"Found {schemas.Count} vendor schemas: {string.Join(", ", schemas)}");
        Assert.True(schemas.Count >= 10, $"Expected at least 10 vendor schemas, got {schemas.Count}");
    }

    [Fact]
    public async Task GetVendorSchemas_IncludesKnownSchemas()
    {
        // Arrange
        var service = _fixture.GetService<ISchemaDiscoveryService>();
        var expectedSchemas = new[] { "boehmers", "sto", "kawneer", "boise_cascade" };

        // Act
        var schemas = await service.GetVendorSchemasAsync();

        // Assert
        foreach (var expected in expectedSchemas)
        {
            Assert.Contains(expected, schemas, StringComparer.OrdinalIgnoreCase);
            _output.WriteLine($"✓ Found schema: {expected}");
        }
    }

    [Fact]
    public async Task GetVendorSchemas_CachesResult()
    {
        // Arrange
        var service = _fixture.GetService<ISchemaDiscoveryService>();

        // Act
        var firstCall = await service.GetVendorSchemasAsync();
        var secondCall = await service.GetVendorSchemasAsync();

        // Assert
        Assert.Same(firstCall, secondCall); // Should return cached instance
    }
}
