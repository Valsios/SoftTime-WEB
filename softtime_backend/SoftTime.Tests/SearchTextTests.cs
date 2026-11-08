using SoftTime.Domain.Services;

namespace SoftTime.Tests;

public class SearchTextTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptySearchMatchesEverything(string? search)
    {
        // Act
        var result = SearchText.Matches(search, "Dupont", "Jean");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void MatchesIsCaseAndAccentInsensitive()
    {
        // Act & Assert
        SearchText.Matches("rene", "René Dupont").Should().BeTrue();
        SearchText.Matches("DUPONT", "René Dupont").Should().BeTrue();
        SearchText.Matches("é", "Rene").Should().BeTrue();
    }

    [Fact]
    public void MatchesRequiresEveryWord()
    {
        // Act & Assert
        SearchText.Matches("rene dupont", "René", "Dupont").Should().BeTrue();
        SearchText.Matches("rene martin", "René", "Dupont").Should().BeFalse();
    }

    [Fact]
    public void RawTermsPreserveAccentsForSql()
    {
        // Act
        var result = SearchText.RawTerms(" René ");

        // Assert
        result.Should().Equal(new[] { "René" });
        SearchText.Terms(" René ").Should().Equal(new[] { "rene" });
    }

    [Fact]
    public void MatchesObjectSearchesPublicValuesButIgnoresTechnicalId()
    {
        // Arrange
        var row = new SearchRow(42, "Dupont", "Rene");

        // Act & Assert
        SearchText.MatchesObject("dupont", row).Should().BeTrue();
        SearchText.MatchesObject("42", row).Should().BeFalse();
    }

    [Fact]
    public void NormalizeCapsLength()
    {
        // Act
        var normalized = SearchText.Normalize(new string('a', SearchText.MaxLength + 20));

        // Assert
        normalized.Should().NotBeNull();
        normalized!.Length.Should().Be(SearchText.MaxLength);
    }

    private sealed record SearchRow(int Id, string Nom, string Prenom);
}
