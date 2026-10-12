using SoftTime.Domain.Services;

namespace SoftTime.Tests;

public class DashboardRulesTests
{
    [Fact]
    public void TauxAbsenteisme_IsAbsenceDaysOverPlannedDays()
    {
        Assert.Equal(5m, DashboardRules.TauxAbsenteisme(1, 20));
        Assert.Equal(50m, DashboardRules.TauxAbsenteisme(5, 10));
    }

    [Fact]
    public void TauxAbsenteisme_WithoutPlanning_IsZero()
    {
        Assert.Equal(0m, DashboardRules.TauxAbsenteisme(3, 0));
    }

    [Fact]
    public void TauxPonctualite_IsWorkedDaysWithoutLateOverWorkedDays()
    {
        // 20 jours travaillés dont 1 en retard ⇒ 19/20 = 95 %.
        Assert.Equal(95m, DashboardRules.TauxPonctualite(20, 1));
        Assert.Equal(100m, DashboardRules.TauxPonctualite(12, 0));
    }

    [Fact]
    public void TauxPonctualite_WithoutWorkedDays_IsZero()
    {
        Assert.Equal(0m, DashboardRules.TauxPonctualite(0, 0));
    }

    [Fact]
    public void Pourcentage_IsRoundedTo1Decimal()
    {
        // 1/3 = 33.333… ⇒ 33.3 (et non 33.3 % tronqué).
        Assert.Equal(33.3m, DashboardRules.Pourcentage(1, 3));
        Assert.Equal(66.7m, DashboardRules.Pourcentage(2, 3));
    }

    [Fact]
    public void Taux_IsClampedBetweenZeroAndHundred()
    {
        // Un retard compté deux fois ne doit pas produire un taux négatif.
        Assert.Equal(0m, DashboardRules.TauxPonctualite(5, 9));
        Assert.Equal(100m, DashboardRules.TauxAbsenteisme(30, 10));
    }
}
