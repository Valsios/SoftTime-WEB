using SoftTime.Domain.Services;

namespace SoftTime.Tests;

/// <summary>
/// Calcul du retard à l'entrée, règle unique partagée par la validation semaine, le tableau de
/// bord et l'état retards.
/// </summary>
public class RetardRulesTests
{
    private static readonly TimeSpan HuitHeures = new(8, 0, 0);

    [Fact]
    public void Calculer_SansEntree_RetourneNull()
        => Assert.Null(RetardRules.Calculer(null, HuitHeures, 0));

    [Fact]
    public void Calculer_SansDebutPrevu_RetourneNull()
        => Assert.Null(RetardRules.Calculer(new TimeSpan(8, 30, 0), null, 0));

    [Fact]
    public void Calculer_ALHeure_RetourneZero()
        => Assert.Equal(TimeSpan.Zero, RetardRules.Calculer(new TimeSpan(8, 0, 0), HuitHeures, 0));

    [Fact]
    public void Calculer_AvantLHeure_RetourneZero()
        => Assert.Equal(TimeSpan.Zero, RetardRules.Calculer(new TimeSpan(7, 40, 0), HuitHeures, 0));

    [Fact]
    public void Calculer_DansLaTolerance_RetourneZero()
        => Assert.Equal(TimeSpan.Zero, RetardRules.Calculer(new TimeSpan(8, 10, 0), HuitHeures, 10));

    [Fact]
    public void Calculer_AuDelaDeLaTolerance_RetourneLEcart()
        => Assert.Equal(TimeSpan.FromMinutes(15),
            RetardRules.Calculer(new TimeSpan(8, 25, 0), HuitHeures, 10));

    [Fact]
    public void Calculer_ToleranceNegative_EstIgnoree()
        => Assert.Equal(TimeSpan.FromMinutes(5),
            RetardRules.Calculer(new TimeSpan(8, 5, 0), HuitHeures, -10));

    [Fact]
    public void Calculer_ExactementALaTolerance_RetourneZero()
        => Assert.Equal(TimeSpan.Zero, RetardRules.Calculer(new TimeSpan(8, 10, 0), HuitHeures, 10));
}
