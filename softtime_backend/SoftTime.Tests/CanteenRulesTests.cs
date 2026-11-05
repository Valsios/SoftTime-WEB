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
        => Assert.True(CanteenRules.DansFenetre(new TimeSpan(h, m, 0)));

    [Theory]
    [InlineData(10, 59)]
    [InlineData(14, 31)]
    [InlineData(7, 0)]
    [InlineData(23, 0)]
    public void DansFenetre_RejectsHoursOutsideMealWindow(int h, int m)
        => Assert.False(CanteenRules.DansFenetre(new TimeSpan(h, m, 0)));

    // ---- Absence complète (règle du desktop : AbsenceAm && AbsencePm) ----

    [Fact]
    public void EstAbsenceComplete_RequiresBothHalves()
        => Assert.True(CanteenRules.EstAbsenceComplete(true, true));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void EstAbsenceComplete_RejectsPartialOrNoAbsence(bool am, bool pm)
        => Assert.False(CanteenRules.EstAbsenceComplete(am, pm));

    [Theory]
    [InlineData(null, null)]
    [InlineData(true, null)]
    [InlineData(null, true)]
    public void EstAbsenceComplete_RejectsMissingFlags(bool? am, bool? pm)
        => Assert.False(CanteenRules.EstAbsenceComplete(am, pm));

    // ---- Détection d'anomalies ----

    [Fact]
    public void DetecterAnomalies_FlagsPunchOnFullDayAbsence()
    {
        var pointes = new[] { new CanteenDay("369", Lundi) };
        var absences = new[] { new CanteenDay("369", Lundi) };

        var anomalies = CanteenRules.DetecterAnomalies(pointes, absences);

        var anomalie = Assert.Single(anomalies);
        Assert.Equal("369", anomalie.Matricule);
        Assert.Equal(Lundi, anomalie.Date);
        Assert.Equal(CanteenRules.MotifAbsenceContredite, anomalie.Motif);
    }

    [Fact]
    public void DetecterAnomalies_NoPunchMeansNoAnomaly()
    {
        // Une absence complète sans pointage est une journée normale : elle ne doit pas remonter.
        var anomalies = CanteenRules.DetecterAnomalies(
            Array.Empty<CanteenDay>(), new[] { new CanteenDay("369", Lundi) });
        Assert.Empty(anomalies);
    }

    [Fact]
    public void DetecterAnomalies_IgnoresOtherEmployees()
    {
        var pointes = new[] { new CanteenDay("369", Lundi) };
        var absences = new[] { new CanteenDay("412", Lundi) };
        Assert.Empty(CanteenRules.DetecterAnomalies(pointes, absences));
    }

    [Fact]
    public void DetecterAnomalies_IgnoresDifferentDays()
    {
        var pointes = new[] { new CanteenDay("369", Lundi) };
        var absences = new[] { new CanteenDay("369", Lundi.AddDays(1)) };
        Assert.Empty(CanteenRules.DetecterAnomalies(pointes, absences));
    }

    [Fact]
    public void DetecterAnomalies_IgnoresTimePart()
    {
        var pointes = new[] { new CanteenDay("369", Lundi.AddHours(12)) };
        var absences = new[] { new CanteenDay("369", Lundi) };

        var anomalie = Assert.Single(CanteenRules.DetecterAnomalies(pointes, absences));
        Assert.Equal(Lundi, anomalie.Date);
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

        Assert.Equal(3, anomalies.Count);
        Assert.Equal("369", anomalies[0].Matricule);
        Assert.Equal(Lundi, anomalies[0].Date);
        Assert.Equal(Lundi.AddDays(1), anomalies[1].Date);
        Assert.Equal("412", anomalies[2].Matricule);
    }

    [Fact]
    public void DetecterAnomalies_EmptyInputsYieldNothing()
        => Assert.Empty(CanteenRules.DetecterAnomalies(Array.Empty<CanteenDay>(), Array.Empty<CanteenDay>()));
}
