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
        Assert.Equal((1, 50), PaginationRules.Normaliser(null, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void Normaliser_RejectsInvalidPage(int page)
    {
        var (pageNormalisee, taille) = PaginationRules.Normaliser(page, 20);
        Assert.Equal(1, pageNormalisee);
        Assert.Equal(20, taille);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Normaliser_RejectsInvalidPageSize(int taille)
        => Assert.Equal((2, PaginationRules.TailleDefaut), PaginationRules.Normaliser(2, taille));

    [Theory]
    [InlineData(101)]
    [InlineData(5000)]
    public void Normaliser_CapsPageSizeAtMaximum(int taille)
        => Assert.Equal(PaginationRules.TailleMax, PaginationRules.Normaliser(1, taille).Taille);

    [Fact]
    public void Normaliser_KeepsValidValues()
    {
        Assert.Equal((3, 100), PaginationRules.Normaliser(3, 100));
        Assert.Equal((7, 25), PaginationRules.Normaliser(7, 25));
    }

    [Theory]
    [InlineData(1, 50, 0)]
    [InlineData(2, 50, 50)]
    [InlineData(3, 25, 50)]
    public void Decalage_ComputesTheSkipOffset(int page, int taille, int attendu)
        => Assert.Equal(attendu, PaginationRules.Decalage(page, taille));

    [Theory]
    [InlineData(0, 50, 1)]
    [InlineData(1, 50, 1)]
    [InlineData(50, 50, 1)]
    [InlineData(51, 50, 2)]
    [InlineData(100, 50, 2)]
    [InlineData(101, 50, 3)]
    public void NombreDePages_AlwaysReturnsAtLeastOnePage(int total, int taille, int attendu)
        => Assert.Equal(attendu, PaginationRules.NombreDePages(total, taille));
}
