using Microsoft.Extensions.DependencyInjection;
using SourcingEngine.Core.Repositories;
using SourcingEngine.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace SourcingEngine.Tests.Integration;

/// <summary>
/// Integration tests for MaterialFamilyRepository - requires database
/// </summary>
[Collection("Database")]
[Trait("Category", "Integration")]
public class MaterialFamilyRepositoryTests
{
    private readonly DatabaseFixture _fixture;
    private readonly ITestOutputHelper _output;

    public MaterialFamilyRepositoryTests(DatabaseFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Theory]
    [InlineData("cmu", "cmu")]
    [InlineData("masonry", "cmu")]
    [InlineData("stucco", "stucco")]
    [InlineData("joist", "joist")]
    [InlineData("railing", "railing")]
    public async Task FindByKeywords_KnownKeyword_ReturnsFamilyContainingKeyword(string keyword, string expectedInLabel)
    {
        // Arrange
        using var scope = _fixture.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMaterialFamilyRepository>();

        // Act
        var families = await repository.FindByKeywordsAsync([keyword]);

        // Assert
        _output.WriteLine($"Search for '{keyword}' returned {families.Count} families:");
        foreach (var family in families)
        {
            _output.WriteLine($"  - {family.FamilyLabel}: {family.FamilyName}");
        }

        Assert.True(families.Count >= 1, $"Expected at least 1 family for keyword '{keyword}'");
        Assert.Contains(families, f => 
            f.FamilyLabel.Contains(expectedInLabel, StringComparison.OrdinalIgnoreCase) ||
            (f.FamilyName?.Contains(expectedInLabel, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    [Fact]
    public async Task FindByKeywords_EmptyList_ReturnsEmpty()
    {
        // Arrange
        using var scope = _fixture.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMaterialFamilyRepository>();

        // Act
        var families = await repository.FindByKeywordsAsync([]);

        // Assert
        Assert.Empty(families);
    }

    [Fact]
    public async Task FindByKeywords_UnknownKeyword_ReturnsEmpty()
    {
        // Arrange
        using var scope = _fixture.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMaterialFamilyRepository>();

        // Act
        var families = await repository.FindByKeywordsAsync(["nonexistentkeyword12345"]);

        // Assert
        Assert.Empty(families);
    }
}
