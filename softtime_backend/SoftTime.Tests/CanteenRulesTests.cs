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
        => CanteenRules.DansFenetre(new TimeSpan(h, m, 0)).Should().BeTrue();

    [Theory]
    [InlineData(10, 59)]
    [InlineData(14, 31)]
    [InlineData(7, 0)]
    [InlineData(23, 0)]
    public void DansFenetre_RejectsHoursOutsideMealWindow(int h, int m)
        => CanteenRules.DansFenetre(new TimeSpan(h, m, 0)).Should().BeFalse();

    // ---- Absence complète (règle du desktop : AbsenceAm && AbsencePm) ----

    [Fact]
    public void EstAbsenceComplete_RequiresBothHalves()
        => CanteenRules.EstAbsenceComplete(true, true).Should().BeTrue();

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void EstAbsenceComplete_RejectsPartialOrNoAbsence(bool am, bool pm)
        => CanteenRules.EstAbsenceComplete(am, pm).Should().BeFalse();

    [Theory]
    [InlineData(null, null)]
    [InlineData(true, null)]
    [InlineData(null, true)]
    public void EstAbsenceComplete_RejectsMissingFlags(bool? am, bool? pm)
        => CanteenRules.EstAbsenceComplete(am, pm).Should().BeFalse();

    // ---- Détection d'anomalies ----

    [Fact]
    public void DetecterAnomalies_FlagsPunchOnFullDayAbsence()
    {
        var pointes = new[] { new CanteenDay("369", Lundi) };
        var absences = new[] { new CanteenDay("369", Lundi) };

        var anomalies = CanteenRules.DetecterAnomalies(pointes, absences);

        var anomalie = anomalies.Should().ContainSingle().Which;
        anomalie.Matricule.Should().Be("369");
        anomalie.Date.Should().Be(Lundi);
        anomalie.Motif.Should().Be(CanteenRules.MotifAbsenceContredite);
    }

    [Fact]
    public void DetecterAnomalies_NoPunchMeansNoAnomaly()
    {
        // Une absence complète sans pointage est une journée normale : elle ne doit pas remonter.
        var anomalies = CanteenRules.DetecterAnomalies(
            Array.Empty<CanteenDay>(), new[] { new CanteenDay("369", Lundi) });
        anomalies.Should().BeEmpty();
    }

    [Fact]
    public void DetecterAnomalies_IgnoresOtherEmployees()
    {
        var pointes = new[] { new CanteenDay("369", Lundi) };
        var absences = new[] { new CanteenDay("412", Lundi) };
        CanteenRules.DetecterAnomalies(pointes, absences).Should().BeEmpty();
    }

    [Fact]
    public void DetecterAnomalies_IgnoresDifferentDays()
    {
        var pointes = new[] { new CanteenDay("369", Lundi) };
        var absences = new[] { new CanteenDay("369", Lundi.AddDays(1)) };
        CanteenRules.DetecterAnomalies(pointes, absences).Should().BeEmpty();
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

        var anomalies = CanteenRules.DetecterAnomalies(pointes, absences);

        anomalies.Count.Should().Be(3);
        anomalies[0].Matricule.Should().Be("369");
        anomalies[0].Date.Should().Be(Lundi);
        anomalies[1].Date.Should().Be(Lundi.AddDays(1));
        anomalies[2].Matricule.Should().Be("412");
    }

    [Fact]
    public void DetecterAnomalies_EmptyInputsYieldNothing()
        => CanteenRules.DetecterAnomalies(Array.Empty<CanteenDay>(), Array.Empty<CanteenDay>()).Should().BeEmpty();
}
