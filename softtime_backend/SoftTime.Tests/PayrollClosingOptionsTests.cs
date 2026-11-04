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

    private static readonly T_HS_VALIDER SemaineValidee =
        new() { MATRICULES = "00369", DATE_DEB = Lundi, DATE_FIN = Lundi.AddDays(6), VALIDATION_HS = true };

    private static IReadOnlyList<ClosingAnomaly> Incomplets(
        ClosingRulesOptions? options,
        IReadOnlyList<T_HEURECORRIGER>? corrections = null)
        => PayrollClosingRules.PointagesIncomplets(
            new[] { new T_CARDPAIE { ID = 1, SAGE_MATRICULE = "00369" } },
            Array.Empty<T_PLANNING_SAL>(),
            new[] { new T_POINTAGE { MATRICULE_SAGE = "369", DATE_POINTAGE = Mercredi, HEURE_POINTAGE = TimeSpan.FromHours(8) } },
            corrections ?? Array.Empty<T_HEURECORRIGER>(),
            new[] { SemaineValidee },
            From, To, multiPointage: false, options);

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
        var manuelle = new T_HEURECORRIGER { MATRICULE_SAGE = "369", DATE_POINTAGE_IN = Mercredi, ORIGINE = "MANUEL" };

        Assert.Empty(Incomplets(new ClosingRulesOptions { IgnorerSemainesValidees = false }, new[] { manuelle }));
    }
}
