using SoftTime.Domain.Services;

namespace SoftTime.Tests;

public class DashboardRulesTests
{
    [Fact]
    public void TauxAbsenteisme_AbsenceAndPlannedDays_ReturnsTheirRatio()
    {
        // Act & Assert
        DashboardRules.TauxAbsenteisme(1, 20).Should().Be(5m);
        DashboardRules.TauxAbsenteisme(5, 10).Should().Be(50m);
    }

    [Fact]
    public void TauxAbsenteisme_WithoutPlanning_IsZero()
    {
        // Act
        var result = DashboardRules.TauxAbsenteisme(3, 0);

        // Assert
        result.Should().Be(0m);
    }

    [Fact]
    public void CompteDansPresenceTheorique_HolidayOrSageLeave_ReturnsFalse()
    {
        // Act & Assert
        // Seul un jour réellement planifié et attendu alimente le dénominateur du taux d'absentéisme.
        DashboardRules.CompteDansPresenceTheorique(estNonTravaille: false, estConge: false).Should().BeTrue();
        DashboardRules.CompteDansPresenceTheorique(estNonTravaille: true, estConge: false).Should().BeFalse();
        DashboardRules.CompteDansPresenceTheorique(estNonTravaille: false, estConge: true).Should().BeFalse();
        DashboardRules.CompteDansPresenceTheorique(estNonTravaille: true, estConge: true).Should().BeFalse();
    }

    [Fact]
    public void TauxPonctualite_WorkedAndLateDays_ReturnsOnTimeRatio()
    {
        // Act & Assert
        // 20 jours travaillés dont 1 en retard ⇒ 19/20 = 95 %.
        DashboardRules.TauxPonctualite(20, 1).Should().Be(95m);
        DashboardRules.TauxPonctualite(12, 0).Should().Be(100m);
    }

    [Fact]
    public void TauxPonctualite_WithoutWorkedDays_IsZero()
    {
        // Act
        var result = DashboardRules.TauxPonctualite(0, 0);

        // Assert
        result.Should().Be(0m);
    }

    [Fact]
    public void Pourcentage_AnyRatio_IsRoundedToOneDecimal()
    {
        // Act & Assert
        // 1/3 = 33.333… ⇒ 33.3 (et non 33.3 % tronqué).
        DashboardRules.Pourcentage(1, 3).Should().Be(33.3m);
        DashboardRules.Pourcentage(2, 3).Should().Be(66.7m);
    }

    [Fact]
    public void TauxPonctualite_OutOfRangeInputs_IsClampedBetweenZeroAndHundred()
    {
        // Act
        var result = DashboardRules.TauxPonctualite(5, 9);

        // Assert
        // Un retard compté deux fois ne doit pas produire un taux négatif.
        result.Should().Be(0m);
        DashboardRules.TauxAbsenteisme(30, 10).Should().Be(100m);
    }
}
