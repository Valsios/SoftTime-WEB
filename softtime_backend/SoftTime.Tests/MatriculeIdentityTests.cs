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
    public void Cle_EquivalentMatricules_ProduceSameKey(string gauche, string droite)
    {
        // Act
        var result = MatriculeIdentity.Cle(droite);

        // Assert
        result.Should().Be(MatriculeIdentity.Cle(gauche));
    }

    [Theory]
    [InlineData("369")]
    [InlineData("0369")]
    [InlineData(" 00369 ")]
    public void Cle_NumericMatricule_IsNormalized(string matricule)
    {
        // Act
        var result = MatriculeIdentity.Cle(matricule);

        // Assert
        result.Should().Be("369");
    }

    [Fact]
    public void Cle_NonNumericMatricule_KeepsLeadingZeros()
    {
        // Act
        var result = MatriculeIdentity.Cle("a0369");

        // Assert
        result.Should().Be("A0369");
    }

    [Fact]
    public void Cle_NullOrEmpty_ReturnsEmpty()
    {
        // Act & Assert
        MatriculeIdentity.Cle(null).Should().Be(string.Empty);
        MatriculeIdentity.Cle("   ").Should().Be(string.Empty);
    }

    [Fact]
    public void MemeSalarie_PaddedAndUnpadded_ReturnsTrue()
    {
        // Act
        var result = MatriculeIdentity.MemeSalarie("00369", "369");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void MemeSalarie_DistinctOrEmpty_ReturnsFalse()
    {
        // Act & Assert
        MatriculeIdentity.MemeSalarie("369", "412").Should().BeFalse();
        MatriculeIdentity.MemeSalarie(null, null).Should().BeFalse();
        MatriculeIdentity.MemeSalarie(string.Empty, string.Empty).Should().BeFalse();
        MatriculeIdentity.MemeSalarie("369", null).Should().BeFalse();
    }
}
