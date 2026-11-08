using SoftTime.Domain.Services;

namespace SoftTime.Tests;

/// <summary>
/// Bornage des paramètres de pagination des listes massives (pointages, heures corrigées).
/// </summary>
public class PaginationRulesTests
{
    [Fact]
    public void Normaliser_FallsBackOnDefaults()
    {
        // Act
        var result = PaginationRules.Normaliser(null, null);

        // Assert
        result.Should().Be((1, 50));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void Normaliser_RejectsInvalidPage(int page)
    {
        // Act
        var (pageNormalisee, taille) = PaginationRules.Normaliser(page, 20);

        // Assert
        pageNormalisee.Should().Be(1);
        taille.Should().Be(20);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Normaliser_RejectsInvalidPageSize(int taille)
    {
        // Act
        var result = PaginationRules.Normaliser(2, taille);

        // Assert
        result.Should().Be((2, PaginationRules.TailleDefaut));
    }

    [Theory]
    [InlineData(101)]
    [InlineData(5000)]
    public void Normaliser_CapsPageSizeAtMaximum(int taille)
        => PaginationRules.Normaliser(1, taille).Taille.Should().Be(PaginationRules.TailleMax);

    [Fact]
    public void Normaliser_KeepsValidValues()
    {
        // Act & Assert
        PaginationRules.Normaliser(3, 100).Should().Be((3, 100));
        PaginationRules.Normaliser(7, 25).Should().Be((7, 25));
    }

    [Fact]
    public void Decalage_PageExtreme_DoesNotOverflow()
    {
        // Act
        var result = PaginationRules.Decalage(int.MaxValue, 100);

        // Assert
        result.Should().Be(int.MaxValue);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    [InlineData(-4, -10)]
    public void Decalage_PageOrSizeInvalid_ReturnsZero(int page, int taille)
    {
        // Act
        var result = PaginationRules.Decalage(page, taille);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void PageEffective_EmptyTotal_ReturnsOne()
    {
        // Act
        var result = PaginationRules.PageEffective(0, 5);

        // Assert
        result.Should().Be(1);
    }

    [Fact]
    public void PageEffective_NonEmpty_ReturnsRequestedPage()
    {
        // Act
        var result = PaginationRules.PageEffective(25, 5);

        // Assert
        result.Should().Be(5);
    }

    [Theory]
    [InlineData(1, 50, 0)]
    [InlineData(2, 50, 50)]
    [InlineData(3, 25, 50)]
    public void Decalage_ComputesTheSkipOffset(int page, int taille, int attendu)
    {
        // Act
        var result = PaginationRules.Decalage(page, taille);

        // Assert
        result.Should().Be(attendu);
    }

    [Theory]
    [InlineData(0, 50, 1)]
    [InlineData(1, 50, 1)]
    [InlineData(50, 50, 1)]
    [InlineData(51, 50, 2)]
    [InlineData(100, 50, 2)]
    [InlineData(101, 50, 3)]
    public void NombreDePages_AlwaysReturnsAtLeastOnePage(int total, int taille, int attendu)
    {
        // Act
        var result = PaginationRules.NombreDePages(total, taille);

        // Assert
        result.Should().Be(attendu);
    }
}
