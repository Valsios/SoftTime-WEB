using SoftTime.Domain.Services;

namespace SoftTime.Tests;

/// <summary>
/// Clé canonique de matricule : les bases SAGE et pointage ne remplissent pas les zéros de la même
/// façon, la comparaison doit rester numérique.
/// </summary>
public class MatriculeIdentityTests
{
    [Theory]
    [InlineData("00369", "369")]
    [InlineData("000", "0")]
    [InlineData("369", " 369 ")]
    [InlineData("ab12", "AB12")]
    public void Cle_ProducesSameKeyForEquivalentMatricules(string gauche, string droite)
        => Assert.Equal(MatriculeIdentity.Cle(gauche), MatriculeIdentity.Cle(droite));

    [Theory]
    [InlineData("369")]
    [InlineData("0369")]
    [InlineData(" 00369 ")]
    public void Cle_NormalizesNumericMatricules(string matricule)
        => Assert.Equal("369", MatriculeIdentity.Cle(matricule));

    [Fact]
    public void Cle_KeepsLeadingZerosOnNonNumericMatricules()
        => Assert.Equal("A0369", MatriculeIdentity.Cle("a0369"));

    [Fact]
    public void Cle_NullOrEmptyYieldsEmpty()
    {
        Assert.Equal(string.Empty, MatriculeIdentity.Cle(null));
        Assert.Equal(string.Empty, MatriculeIdentity.Cle("   "));
    }

    [Fact]
    public void MemeSalarie_MatchesPaddedAndUnpadded()
        => Assert.True(MatriculeIdentity.MemeSalarie("00369", "369"));

    [Fact]
    public void MemeSalarie_RejectsDistinctOrEmpty()
    {
        Assert.False(MatriculeIdentity.MemeSalarie("369", "412"));
        Assert.False(MatriculeIdentity.MemeSalarie(null, null));
        Assert.False(MatriculeIdentity.MemeSalarie(string.Empty, string.Empty));
        Assert.False(MatriculeIdentity.MemeSalarie("369", null));
    }
}
