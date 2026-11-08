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
        SearchText.Matches(search, "Dupont", "Jean").Should().BeTrue();
    }

    [Fact]
    public void MatchesIsCaseAndAccentInsensitive()
    {
        SearchText.Matches("rene", "René Dupont").Should().BeTrue();
        SearchText.Matches("DUPONT", "René Dupont").Should().BeTrue();
        SearchText.Matches("é", "Rene").Should().BeTrue();
    }

    [Fact]
    public void MatchesRequiresEveryWord()
    {
        SearchText.Matches("rene dupont", "René", "Dupont").Should().BeTrue();
        SearchText.Matches("rene martin", "René", "Dupont").Should().BeFalse();
    }

    [Fact]
    public void RawTermsPreserveAccentsForSql()
    {
        SearchText.RawTerms(" René ").Should().Equal(new[] { "René" });
        SearchText.Terms(" René ").Should().Equal(new[] { "rene" });
    }

    [Fact]
    public void MatchesObjectSearchesPublicValuesButIgnoresTechnicalId()
    {
        var row = new SearchRow(42, "Dupont", "Rene");

        SearchText.MatchesObject("dupont", row).Should().BeTrue();
        SearchText.MatchesObject("42", row).Should().BeFalse();
    }

    [Fact]
    public void NormalizeCapsLength()
    {
        var normalized = SearchText.Normalize(new string('a', SearchText.MaxLength + 20));

        normalized.Should().NotBeNull();
        normalized!.Length.Should().Be(SearchText.MaxLength);
    }

    private sealed record SearchRow(int Id, string Nom, string Prenom);
}
