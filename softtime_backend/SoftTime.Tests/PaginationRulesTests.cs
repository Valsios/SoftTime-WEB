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
        PaginationRules.Normaliser(null, null).Should().Be((1, 50));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void Normaliser_RejectsInvalidPage(int page)
    {
        var (pageNormalisee, taille) = PaginationRules.Normaliser(page, 20);
        pageNormalisee.Should().Be(1);
        taille.Should().Be(20);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Normaliser_RejectsInvalidPageSize(int taille)
        => PaginationRules.Normaliser(2, taille).Should().Be((2, PaginationRules.TailleDefaut));

    [Theory]
    [InlineData(101)]
    [InlineData(5000)]
    public void Normaliser_CapsPageSizeAtMaximum(int taille)
        => PaginationRules.Normaliser(1, taille).Taille.Should().Be(PaginationRules.TailleMax);

    [Fact]
    public void Normaliser_KeepsValidValues()
    {
        PaginationRules.Normaliser(3, 100).Should().Be((3, 100));
        PaginationRules.Normaliser(7, 25).Should().Be((7, 25));
    }

    [Fact]
    public void Decalage_PageExtreme_DoesNotOverflow()
        => PaginationRules.Decalage(int.MaxValue, 100).Should().Be(int.MaxValue);

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    [InlineData(-4, -10)]
    public void Decalage_PageOrSizeInvalid_ReturnsZero(int page, int taille)
        => PaginationRules.Decalage(page, taille).Should().Be(0);

    [Fact]
    public void PageEffective_EmptyTotal_ReturnsOne()
        => PaginationRules.PageEffective(0, 5).Should().Be(1);

    [Fact]
    public void PageEffective_NonEmpty_ReturnsRequestedPage()
        => PaginationRules.PageEffective(25, 5).Should().Be(5);

    [Theory]
    [InlineData(1, 50, 0)]
    [InlineData(2, 50, 50)]
    [InlineData(3, 25, 50)]
    public void Decalage_ComputesTheSkipOffset(int page, int taille, int attendu)
        => PaginationRules.Decalage(page, taille).Should().Be(attendu);

    [Theory]
    [InlineData(0, 50, 1)]
    [InlineData(1, 50, 1)]
    [InlineData(50, 50, 1)]
    [InlineData(51, 50, 2)]
    [InlineData(100, 50, 2)]
    [InlineData(101, 50, 3)]
    public void NombreDePages_AlwaysReturnsAtLeastOnePage(int total, int taille, int attendu)
        => PaginationRules.NombreDePages(total, taille).Should().Be(attendu);
}
