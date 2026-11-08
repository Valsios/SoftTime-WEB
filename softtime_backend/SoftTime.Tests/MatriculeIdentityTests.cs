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
        => MatriculeIdentity.Cle(droite).Should().Be(MatriculeIdentity.Cle(gauche));

    [Theory]
    [InlineData("369")]
    [InlineData("0369")]
    [InlineData(" 00369 ")]
    public void Cle_NormalizesNumericMatricules(string matricule)
        => MatriculeIdentity.Cle(matricule).Should().Be("369");

    [Fact]
    public void Cle_KeepsLeadingZerosOnNonNumericMatricules()
        => MatriculeIdentity.Cle("a0369").Should().Be("A0369");

    [Fact]
    public void Cle_NullOrEmptyYieldsEmpty()
    {
        MatriculeIdentity.Cle(null).Should().Be(string.Empty);
        MatriculeIdentity.Cle("   ").Should().Be(string.Empty);
    }

    [Fact]
    public void MemeSalarie_MatchesPaddedAndUnpadded()
        => MatriculeIdentity.MemeSalarie("00369", "369").Should().BeTrue();

    [Fact]
    public void MemeSalarie_RejectsDistinctOrEmpty()
    {
        MatriculeIdentity.MemeSalarie("369", "412").Should().BeFalse();
        MatriculeIdentity.MemeSalarie(null, null).Should().BeFalse();
        MatriculeIdentity.MemeSalarie(string.Empty, string.Empty).Should().BeFalse();
        MatriculeIdentity.MemeSalarie("369", null).Should().BeFalse();
    }
}
