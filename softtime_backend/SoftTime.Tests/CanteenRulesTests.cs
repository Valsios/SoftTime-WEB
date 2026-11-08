using SoftTime.Domain.Models;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

/// <summary>
/// Règles du calcul cantine et détection d'anomalies (portage partiel de
/// <c>CantineService.CalculCantines</c>, adapté au calcul sur <c>T_POINTAGE</c>).
/// </summary>
public class CanteenRulesTests
{
    private static readonly DateTime Lundi = new(2026, 8, 31);

    // ---- Fenêtre du repas ----

    [Theory]
    [InlineData(11, 0)]
    [InlineData(12, 30)]
    [InlineData(14, 30)]
    public void DansFenetre_AcceptsHoursWithinMealWindow(int h, int m)
    {
        // Act
        var result = CanteenRules.DansFenetre(new TimeSpan(h, m, 0));

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(10, 59)]
    [InlineData(14, 31)]
    [InlineData(7, 0)]
    [InlineData(23, 0)]
    public void DansFenetre_RejectsHoursOutsideMealWindow(int h, int m)
    {
        // Act
        var result = CanteenRules.DansFenetre(new TimeSpan(h, m, 0));

        // Assert
        result.Should().BeFalse();
    }

    // ---- Absence complète (règle du desktop : AbsenceAm && AbsencePm) ----

    [Fact]
    public void EstAbsenceComplete_RequiresBothHalves()
    {
        // Act
        var result = CanteenRules.EstAbsenceComplete(true, true);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void EstAbsenceComplete_RejectsPartialOrNoAbsence(bool am, bool pm)
    {
        // Act
        var result = CanteenRules.EstAbsenceComplete(am, pm);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(true, null)]
    [InlineData(null, true)]
    public void EstAbsenceComplete_RejectsMissingFlags(bool? am, bool? pm)
    {
        // Act
        var result = CanteenRules.EstAbsenceComplete(am, pm);

        // Assert
        result.Should().BeFalse();
    }

    // ---- Détection d'anomalies ----

    [Fact]
    public void DetecterAnomalies_FlagsPunchOnFullDayAbsence()
    {
        // Arrange
        var pointes = new[] { new CanteenDay("369", Lundi) };
        var absences = new[] { new CanteenDay("369", Lundi) };

        // Act
        var anomalies = CanteenRules.DetecterAnomalies(pointes, absences);

        // Assert
        var anomalie = anomalies.Should().ContainSingle().Which;
        anomalie.Matricule.Should().Be("369");
        anomalie.Date.Should().Be(Lundi);
        anomalie.Motif.Should().Be(CanteenRules.MotifAbsenceContredite);
    }

    [Fact]
    public void DetecterAnomalies_NoPunchMeansNoAnomaly()
    {
        // Act
        // Une absence complète sans pointage est une journée normale : elle ne doit pas remonter.
        var anomalies = CanteenRules.DetecterAnomalies(
            Array.Empty<CanteenDay>(), new[] { new CanteenDay("369", Lundi) });

        // Assert
        anomalies.Should().BeEmpty();
    }

    [Fact]
    public void DetecterAnomalies_IgnoresOtherEmployees()
    {
        // Arrange
        var pointes = new[] { new CanteenDay("369", Lundi) };
        var absences = new[] { new CanteenDay("412", Lundi) };

        // Act
        var result = CanteenRules.DetecterAnomalies(pointes, absences);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void DetecterAnomalies_IgnoresDifferentDays()
    {
        // Arrange
        var pointes = new[] { new CanteenDay("369", Lundi) };
        var absences = new[] { new CanteenDay("369", Lundi.AddDays(1)) };

        // Act
        var result = CanteenRules.DetecterAnomalies(pointes, absences);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void DetecterAnomalies_IgnoresTimePart()
    {
        var pointes = new[] { new CanteenDay("369", Lundi.AddHours(12)) };
        var absences = new[] { new CanteenDay("369", Lundi) };

        var anomalie = CanteenRules.DetecterAnomalies(pointes, absences).Should().ContainSingle().Which;
        anomalie.Date.Should().Be(Lundi);
    }

    [Fact]
    public void DetecterAnomalies_DeduplicatesAndSorts()
    {
        // Arrange
        var pointes = new[]
        {
            new CanteenDay("412", Lundi),
            new CanteenDay("412", Lundi),
            new CanteenDay("369", Lundi.AddDays(1)),
            new CanteenDay("369", Lundi),
        };
        var absences = new[]
        {
            new CanteenDay("369", Lundi),
            new CanteenDay("369", Lundi.AddDays(1)),
            new CanteenDay("412", Lundi),
        };

        // Act
        var anomalies = CanteenRules.DetecterAnomalies(pointes, absences);

        // Assert
        anomalies.Count.Should().Be(3);
        anomalies[0].Matricule.Should().Be("369");
        anomalies[0].Date.Should().Be(Lundi);
        anomalies[1].Date.Should().Be(Lundi.AddDays(1));
        anomalies[2].Matricule.Should().Be("412");
    }

    [Fact]
    public void DetecterAnomalies_EmptyInputsYieldNothing()
    {
        // Act
        var result = CanteenRules.DetecterAnomalies(Array.Empty<CanteenDay>(), Array.Empty<CanteenDay>());

        // Assert
        result.Should().BeEmpty();
    }
}
