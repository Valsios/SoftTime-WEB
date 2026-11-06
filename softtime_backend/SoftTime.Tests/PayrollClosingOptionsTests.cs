using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

/// <summary>Option <see cref="ClosingRulesOptions.IgnorerSemainesValidees"/> de la règle des pointages impairs.</summary>
public class PayrollClosingOptionsTests
{
    private static readonly DateTime Lundi = new(2025, 11, 17);
    private static readonly DateTime Mercredi = new(2025, 11, 19);
    private static readonly DateTime From = new(2025, 11, 10);
    private static readonly DateTime To = new(2025, 11, 30);

    private static readonly HsValidee SemaineValidee =
        new() { Matricules = "00369", DateDeb = Lundi, DateFin = Lundi.AddDays(6), ValidationHs = true };

    private static IReadOnlyList<ClosingAnomaly> Incomplets(
        ClosingRulesOptions? options,
        IReadOnlyList<HeureCorrigee>? corrections = null)
        => PunchCompletenessRules.PointagesIncomplets(
            new EmployeeSources(new[] { new CardPaie { Id = 1, SageMatricule = "00369" } }, Array.Empty<PlanningSalarie>(), corrections ?? Array.Empty<HeureCorrigee>()),
            new[] { new Pointage { MatriculeSage = "369", DatePointage = Mercredi, HeurePointage = TimeSpan.FromHours(8) } },
            new[] { SemaineValidee },
            new ClosingScope(From, To, false, options));

    [Fact]
    public void IgnoreValidatedWeeks_DefaultIsTrue()
    {
        Assert.True(ClosingRulesOptions.Default.IgnorerSemainesValidees);
        Assert.True(new ClosingRulesOptions().IgnorerSemainesValidees);
    }

    [Fact]
    public void IgnoreValidatedWeeks_True_KeepsCurrentBehaviour()
    {
        Assert.Empty(Incomplets(options: null));
        Assert.Empty(Incomplets(new ClosingRulesOptions { IgnorerSemainesValidees = true }));
    }

    [Fact]
    public void IgnoreValidatedWeeks_False_ReportsDaysOfValidatedWeek()
    {
        var anomalie = Assert.Single(Incomplets(new ClosingRulesOptions { IgnorerSemainesValidees = false }));

        Assert.Equal(ClosingAnomalyType.PointageIncomplet, anomalie.Type);
        Assert.Equal("369", anomalie.Matricule);
        Assert.Equal(Mercredi, anomalie.Date);
    }

    [Fact]
    public void IgnoreValidatedWeeks_False_StillIgnoresManualDays()
    {
        var manuelle = new HeureCorrigee { MatriculeSage = "369", DatePointageIn = Mercredi, Origine = "MANUEL" };

        Assert.Empty(Incomplets(new ClosingRulesOptions { IgnorerSemainesValidees = false }, new[] { manuelle }));
    }
}
