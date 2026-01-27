using Microsoft.Extensions.DependencyInjection;
using SourcingEngine.Core.Repositories;
using SourcingEngine.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace SourcingEngine.Tests.Integration;

/// <summary>
/// Integration tests for ProductRepository - requires database
/// </summary>
[Collection("Database")]
[Trait("Category", "Integration")]
public class ProductRepositoryTests
{
    private readonly DatabaseFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ProductRepositoryTests(DatabaseFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task FindProducts_CmuBlocks_ReturnsMinimumProducts()
    {
        // Arrange
        using var scope = _fixture.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProductRepository>();

        // Act
        var products = await repository.FindProductsAsync(
            familyLabel: "cmu_blocks",
            csiCode: null,
            sizePatterns: null,
            keywords: null);

        // Assert
        _output.WriteLine($"Found {products.Count} CMU block products:");
        foreach (var p in products.Take(10))
        {
            _output.WriteLine($"  - {p.VendorName}: {p.ModelName}");
        }

        Assert.True(products.Count >= 3, $"Expected at least 3 CMU products, got {products.Count}");
    }

    [Fact]
    public async Task FindProducts_WithSizePattern_FiltersCorrectly()
    {
        // Arrange
        using var scope = _fixture.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProductRepository>();

        // Act
        var products = await repository.FindProductsAsync(
            familyLabel: "cmu_blocks",
            csiCode: null,
            sizePatterns: ["20cm", "20 cm"],
            keywords: null);

        // Assert
        _output.WriteLine($"Found {products.Count} 20cm CMU products:");
        foreach (var p in products)
        {
            _output.WriteLine($"  - {p.VendorName}: {p.ModelName}");
        }

        Assert.True(products.Count >= 1, $"Expected at least 1 20cm CMU product, got {products.Count}");
        Assert.All(products, p => Assert.Contains("20", p.ModelName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FindProducts_MultipleVendors_ReturnsDiverseResults()
    {
        // Arrange
        using var scope = _fixture.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProductRepository>();

        // Act
        var products = await repository.FindProductsAsync(
            familyLabel: "cmu_blocks",
            csiCode: null,
            sizePatterns: null,
            keywords: null);

        // Assert
        var distinctVendors = products.Select(p => p.VendorName).Distinct().ToList();
        _output.WriteLine($"Found products from {distinctVendors.Count} vendors: {string.Join(", ", distinctVendors)}");

        Assert.True(distinctVendors.Count >= 2, $"Expected at least 2 distinct vendors, got {distinctVendors.Count}");
    }
}
