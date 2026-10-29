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
        Assert.True(SearchText.Matches(search, "Dupont", "Jean"));
    }

    [Fact]
    public void MatchesIsCaseAndAccentInsensitive()
    {
        Assert.True(SearchText.Matches("rene", "René Dupont"));
        Assert.True(SearchText.Matches("DUPONT", "René Dupont"));
        Assert.True(SearchText.Matches("é", "Rene"));
    }

    [Fact]
    public void MatchesRequiresEveryWord()
    {
        Assert.True(SearchText.Matches("rene dupont", "René", "Dupont"));
        Assert.False(SearchText.Matches("rene martin", "René", "Dupont"));
    }

    [Fact]
    public void RawTermsPreserveAccentsForSql()
    {
        Assert.Equal(new[] { "René" }, SearchText.RawTerms(" René "));
        Assert.Equal(new[] { "rene" }, SearchText.Terms(" René "));
    }

    [Fact]
    public void MatchesObjectSearchesPublicValuesButIgnoresTechnicalId()
    {
        var row = new SearchRow(42, "Dupont", "Rene");

        Assert.True(SearchText.MatchesObject("dupont", row));
        Assert.False(SearchText.MatchesObject("42", row));
    }

    [Fact]
    public void NormalizeCapsLength()
    {
        var normalized = SearchText.Normalize(new string('a', SearchText.MaxLength + 20));

        Assert.NotNull(normalized);
        Assert.Equal(SearchText.MaxLength, normalized!.Length);
    }

    private sealed record SearchRow(int Id, string Nom, string Prenom);
}
