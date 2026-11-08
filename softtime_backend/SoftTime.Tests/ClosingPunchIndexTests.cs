using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

/// <summary>
/// Index des pointages et surcharges indexées de <see cref="PayrollClosingRules"/> : elles doivent
/// produire exactement la même liste (ordre compris) que les surcharges par liste de la phase 0.
/// </summary>
public class ClosingPunchIndexTests
{
    private static readonly DateTime Today = new(2025, 11, 20);
    private static readonly DateTime Lundi = new(2025, 11, 17);
    private static readonly DateTime From = new(2025, 11, 10);
    private static readonly DateTime To = new(2025, 11, 19);
    private static readonly TimeSpan H8 = TimeSpan.FromHours(8);
    private static readonly TimeSpan H17 = TimeSpan.FromHours(17);

    private static Pointage P(string m, DateTime d, TimeSpan h) => new() { MatriculeSage = m, DatePointage = d, HeurePointage = h };

    private static PlanningSalarie Plan(int id, int idsal, DateTime d, TimeSpan ha, TimeSpan hd, bool off = false) =>
        new() { Id = id, IdSal = idsal, DateP = d, Ha = d.Date.Add(ha), Hd = d.Date.Add(hd), OffP = off };

    // Jeu mêlant doublons, quasi-doublons, impairs, jours hors période, sans planning, matricules
    // de formats différents, lignes inexploitables et ordre d'entrée volontairement désordonné.
    private static readonly CardPaie[] Cartes =
    {
        new() { Id = 1, SageMatricule = "00369" }, new() { Id = 2, SageMatricule = "500" }, new() { Id = 3, SageMatricule = "A12" }
    };

    private static readonly PlanningSalarie[] Plannings =
    {
        Plan(1, 1, Lundi, H8, H17), Plan(2, 1, Lundi.AddDays(1), H8, H17), Plan(3, 2, Lundi, H8, H17),
        Plan(4, 2, Lundi.AddDays(1), TimeSpan.FromHours(22), TimeSpan.FromHours(6)), Plan(5, 3, Lundi, H8, H8),
        Plan(6, 1, Lundi.AddDays(2), H8, H17), Plan(7, 2, Lundi.AddDays(2), H8, H17, off: true)
    };

    private static readonly Pointage[] Pointages =
    {
        P("369", Lundi, H17), P("00369", Lundi, H8), P("369", Lundi, H8), P("369", Lundi, H8.Add(TimeSpan.FromSeconds(20))),
        P("500", Lundi.AddDays(1), TimeSpan.FromHours(22)), P("500", Lundi.AddDays(2), TimeSpan.FromHours(6)),
        P("500", Lundi.AddDays(2), TimeSpan.FromHours(6).Add(TimeSpan.FromSeconds(29))),
        P("a12", Lundi, H8), P("A12", Lundi.AddDays(1), H8), P("A12", Lundi.AddDays(1), H17), P("A12", Lundi.AddDays(1), H17),
        P("369", Lundi.AddDays(1), H8), P("369", To.AddDays(1), H8), P("369", To.AddDays(1), H8), P("369", From.AddDays(-1), H8),
        P("999", Lundi, H8), P("999", Lundi, H8),
        new() { MatriculeSage = " ", DatePointage = Lundi, HeurePointage = H8 },
        new() { MatriculeSage = "369", DatePointage = null, HeurePointage = H8 },
        new() { MatriculeSage = "369", DatePointage = Lundi, HeurePointage = null }
    };

    private static readonly HeureCorrigee[] Corrections =
    {
        new() { MatriculeSage = "500", DatePointageIn = Lundi, Origine = "AUTO", HEntree = H8, HSortie = H17 },
        new() { MatriculeSage = "369", DatePointageIn = Lundi.AddDays(2), Origine = "MANUEL", AbsenceAm = true }
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
        PunchCompletenessRules.PointagesIncomplets(new EmployeeSources(Cartes, Plannings, Corrections), Index, Array.Empty<HsValidee>(), new ClosingScope(From, To, multi, options)).Should().Equal(PunchCompletenessRules.PointagesIncomplets(new EmployeeSources(Cartes, Plannings, Corrections), Pointages, Array.Empty<HsValidee>(), new ClosingScope(From, To, multi, options)));
    }

    [Fact]
    public void MissingAndPlannedWithoutPunch_SharedAbsenceLines_SameAsListOverloads()
    {
        var lignes = AbsenceReportRules.Calculer(new EmployeeSources(Cartes, Plannings, Corrections), new AbsenceCalendar(Conges, Feries), new ReportPeriod(From, To, Today));

        PunchCompletenessRules.PointagesManquants(lignes, Index).Should().Equal(PunchCompletenessRules.PointagesManquants(new EmployeeSources(Cartes, Plannings, Corrections), Pointages, new AbsenceCalendar(Conges, Feries), new ReportPeriod(From, To, Today)));
        PlanningCoverageRules.PlanningsSansPointage(lignes, Index, Corrections).Should().Equal(PlanningCoverageRules.PlanningsSansPointage(new EmployeeSources(Cartes, Plannings, Corrections), Pointages, new AbsenceCalendar(Conges, Feries), new ReportPeriod(From, To, Today)));
    }

    [Fact]
    public void PunchRules_IndexOverloads_SameAsListOverloads()
    {
        PlanningCoverageRules.PointagesSansPlanning(Cartes, Plannings, Index, new ClosingScope(From, To)).Should().Equal(PlanningCoverageRules.PointagesSansPlanning(Cartes, Plannings, Pointages, new ClosingScope(From, To)));
        DuplicatePunchRules.DoublonsExacts(Index, new ClosingScope(From, To)).Should().Equal(DuplicatePunchRules.DoublonsExacts(Pointages, new ClosingScope(From, To)));
        DuplicatePunchRules.QuasiDoublons(Index, new ClosingScope(From, To)).Should().Equal(DuplicatePunchRules.QuasiDoublons(Pointages, new ClosingScope(From, To)));
    }

    [Fact]
    public void Rules_ProduceExpectedAnomaliesOnMixedSet()
    {
        var exacts = DuplicatePunchRules.DoublonsExacts(Index, new ClosingScope(From, To));
        var quasi = DuplicatePunchRules.QuasiDoublons(Index, new ClosingScope(From, To));

        // 369 lundi 08:00 ×2, A12 mardi 17:00 ×2, 999 lundi 08:00 ×2 ; 369 du 20/11 hors période.
        exacts.Select(a => (a.Matricule, a.Date)).Should().Equal(new[] { ("369", Lundi), ("999", Lundi), ("A12", Lundi.AddDays(1)) });
        quasi.Select(a => (a.Matricule, a.Date)).Should().Equal(new[] { ("369", Lundi), ("500", Lundi.AddDays(2)) });
    }

    [Fact]
    public void Index_GroupsByCanonicalMatriculeSortedAndCountsPerDay()
    {
        Index.Moments("369").Where(m => m.Date == Lundi).Select(m => m.Heure).Should().Equal(new[] { H8, H8, H8.Add(TimeSpan.FromSeconds(20)), H17 });
        Index.APointage("369", Lundi.AddHours(10)).Should().BeTrue();
        Index.APointage("369", Lundi.AddDays(2)).Should().BeFalse();
        Index.Jours.Should().Contain(("A12", Lundi.AddDays(1), 3));
        Index.Moments("inconnu").Should().BeEmpty();
        Index.Cles.Should().NotContain("");
    }
}
