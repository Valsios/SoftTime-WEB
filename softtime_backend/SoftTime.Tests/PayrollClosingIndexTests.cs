using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

/// <summary>
/// Index des pointages et surcharges indexées de <see cref="PayrollClosingRules"/> : elles doivent
/// produire exactement la même liste (ordre compris) que les surcharges par liste de la phase 0.
/// </summary>
public class PayrollClosingIndexTests
{
    private static readonly DateTime Today = new(2025, 11, 20);
    private static readonly DateTime Lundi = new(2025, 11, 17);
    private static readonly DateTime From = new(2025, 11, 10);
    private static readonly DateTime To = new(2025, 11, 19);
    private static readonly TimeSpan H8 = TimeSpan.FromHours(8);
    private static readonly TimeSpan H17 = TimeSpan.FromHours(17);

    private static T_POINTAGE P(string m, DateTime d, TimeSpan h) => new() { MATRICULE_SAGE = m, DATE_POINTAGE = d, HEURE_POINTAGE = h };

    private static T_PLANNING_SAL Plan(int id, int idsal, DateTime d, TimeSpan ha, TimeSpan hd, bool off = false) =>
        new() { ID = id, IDSAL = idsal, DateP = d, HA = d.Date.Add(ha), HD = d.Date.Add(hd), OffP = off };

    // Jeu mêlant doublons, quasi-doublons, impairs, jours hors période, sans planning, matricules
    // de formats différents, lignes inexploitables et ordre d'entrée volontairement désordonné.
    private static readonly T_CARDPAIE[] Cartes =
    {
        new() { ID = 1, SAGE_MATRICULE = "00369" }, new() { ID = 2, SAGE_MATRICULE = "500" }, new() { ID = 3, SAGE_MATRICULE = "A12" }
    };

    private static readonly T_PLANNING_SAL[] Plannings =
    {
        Plan(1, 1, Lundi, H8, H17), Plan(2, 1, Lundi.AddDays(1), H8, H17), Plan(3, 2, Lundi, H8, H17),
        Plan(4, 2, Lundi.AddDays(1), TimeSpan.FromHours(22), TimeSpan.FromHours(6)), Plan(5, 3, Lundi, H8, H8),
        Plan(6, 1, Lundi.AddDays(2), H8, H17), Plan(7, 2, Lundi.AddDays(2), H8, H17, off: true)
    };

    private static readonly T_POINTAGE[] Pointages =
    {
        P("369", Lundi, H17), P("00369", Lundi, H8), P("369", Lundi, H8), P("369", Lundi, H8.Add(TimeSpan.FromSeconds(30))),
        P("500", Lundi.AddDays(1), TimeSpan.FromHours(22)), P("500", Lundi.AddDays(2), TimeSpan.FromHours(6)),
        P("500", Lundi.AddDays(2), TimeSpan.FromHours(6).Add(TimeSpan.FromSeconds(119))),
        P("a12", Lundi, H8), P("A12", Lundi.AddDays(1), H8), P("A12", Lundi.AddDays(1), H17), P("A12", Lundi.AddDays(1), H17),
        P("369", Lundi.AddDays(1), H8), P("369", To.AddDays(1), H8), P("369", To.AddDays(1), H8), P("369", From.AddDays(-1), H8),
        P("999", Lundi, H8), P("999", Lundi, H8),
        new() { MATRICULE_SAGE = " ", DATE_POINTAGE = Lundi, HEURE_POINTAGE = H8 },
        new() { MATRICULE_SAGE = "369", DATE_POINTAGE = null, HEURE_POINTAGE = H8 },
        new() { MATRICULE_SAGE = "369", DATE_POINTAGE = Lundi, HEURE_POINTAGE = null }
    };

    private static readonly T_HEURECORRIGER[] Corrections =
    {
        new() { MATRICULE_SAGE = "500", DATE_POINTAGE_IN = Lundi, ORIGINE = "AUTO", HENTREE = H8, HSORTIE = H17 },
        new() { MATRICULE_SAGE = "369", DATE_POINTAGE_IN = Lundi.AddDays(2), ORIGINE = "MANUEL", ABSENCE_AM = true }
    };

    private static readonly Dictionary<string, List<LeaveDay>> Conges = new() { ["500"] = new() { new LeaveDay(Lundi.AddDays(1), 0.5m, "CP") } };
    private static readonly HashSet<DateTime> Feries = new() { Lundi.AddDays(2) };

    private static readonly ClosingPunchIndex Index = ClosingPunchIndex.Build(Pointages);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Incomplete_IndexOverload_SameAsListOverload(bool multi)
    {
        var options = new ClosingRulesOptions { IgnorerSemainesValidees = false };
        Assert.Equal(
            PayrollClosingRules.PointagesIncomplets(Cartes, Plannings, Pointages, Corrections, Array.Empty<T_HS_VALIDER>(), From, To, multi, options),
            PayrollClosingRules.PointagesIncomplets(Cartes, Plannings, Index, Corrections, Array.Empty<T_HS_VALIDER>(), From, To, multi, options));
    }

    [Fact]
    public void MissingAndPlannedWithoutPunch_SharedAbsenceLines_SameAsListOverloads()
    {
        var lignes = AbsenceReportRules.Calculer(Plannings, Cartes, Corrections, Conges, Feries, From, To, Today);

        Assert.Equal(
            PayrollClosingRules.PointagesManquants(Cartes, Plannings, Pointages, Corrections, Conges, Feries, From, To, Today),
            PayrollClosingRules.PointagesManquants(lignes, Index));
        Assert.Equal(
            PayrollClosingRules.PlanningsSansPointage(Cartes, Plannings, Pointages, Corrections, Conges, Feries, From, To, Today),
            PayrollClosingRules.PlanningsSansPointage(lignes, Index, Corrections));
    }

    [Fact]
    public void PunchRules_IndexOverloads_SameAsListOverloads()
    {
        Assert.Equal(
            PayrollClosingRules.PointagesSansPlanning(Cartes, Plannings, Pointages, From, To),
            PayrollClosingRules.PointagesSansPlanning(Cartes, Plannings, Index, From, To));
        Assert.Equal(PayrollClosingRules.DoublonsExacts(Pointages, From, To), PayrollClosingRules.DoublonsExacts(Index, From, To));
        Assert.Equal(PayrollClosingRules.QuasiDoublons(Pointages, From, To), PayrollClosingRules.QuasiDoublons(Index, From, To));
    }

    [Fact]
    public void Rules_ProduceExpectedAnomaliesOnMixedSet()
    {
        var exacts = PayrollClosingRules.DoublonsExacts(Index, From, To);
        var quasi = PayrollClosingRules.QuasiDoublons(Index, From, To);

        // 369 lundi 08:00 ×2, A12 mardi 17:00 ×2, 999 lundi 08:00 ×2 ; 369 du 20/11 hors période.
        Assert.Equal(new[] { ("369", Lundi), ("999", Lundi), ("A12", Lundi.AddDays(1)) }, exacts.Select(a => (a.Matricule, a.Date)));
        Assert.Equal(new[] { ("369", Lundi), ("500", Lundi.AddDays(2)) }, quasi.Select(a => (a.Matricule, a.Date)));
    }

    [Fact]
    public void Index_GroupsByCanonicalMatriculeSortedAndCountsPerDay()
    {
        Assert.Equal(
            new[] { H8, H8, H8.Add(TimeSpan.FromSeconds(30)), H17 },
            Index.Moments("369").Where(m => m.Date == Lundi).Select(m => m.Heure));
        Assert.True(Index.APointage("369", Lundi.AddHours(10)));
        Assert.False(Index.APointage("369", Lundi.AddDays(2)));
        Assert.Contains(("A12", Lundi.AddDays(1), 3), Index.Jours);
        Assert.Empty(Index.Moments("inconnu"));
        Assert.DoesNotContain("", Index.Cles);
    }
}
