using SoftTime.Domain.Services;

namespace SoftTime.Tests;

public class SearchTextTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Matches_EmptySearch_MatchesEverything(string? search)
    {
        // Act
        var result = SearchText.Matches(search, "Dupont", "Jean");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Matches_DifferentCaseOrAccents_Matches()
    {
        // Act & Assert
        SearchText.Matches("rene", "René Dupont").Should().BeTrue();
        SearchText.Matches("DUPONT", "René Dupont").Should().BeTrue();
        SearchText.Matches("é", "Rene").Should().BeTrue();
    }

    [Fact]
    public void Matches_SeveralWords_RequiresEveryWord()
    {
        // Act & Assert
        SearchText.Matches("rene dupont", "René", "Dupont").Should().BeTrue();
        SearchText.Matches("rene martin", "René", "Dupont").Should().BeFalse();
    }

    [Fact]
    public void RawTerms_AccentedSearch_PreservesAccents()
    {
        // Act
        var result = SearchText.RawTerms(" René ");

        // Assert
        result.Should().Equal(new[] { "René" });
        SearchText.Terms(" René ").Should().Equal(new[] { "rene" });
    }

    [Fact]
    public void MatchesObject_AnyObject_SearchesPublicValuesButIgnoresTechnicalId()
    {
        // Arrange
        var row = new SearchRow(42, "Dupont", "Rene");

        // Act & Assert
        SearchText.MatchesObject("dupont", row).Should().BeTrue();
        SearchText.MatchesObject("42", row).Should().BeFalse();
    }

    [Fact]
    public void Normalize_LongSearch_CapsLength()
    {
        // Act
        var normalized = SearchText.Normalize(new string('a', SearchText.MaxLength + 20));

        // Assert
        normalized.Should().NotBeNull();
        normalized!.Length.Should().Be(SearchText.MaxLength);
    }

    private sealed record SearchRow(int Id, string Nom, string Prenom);
}
