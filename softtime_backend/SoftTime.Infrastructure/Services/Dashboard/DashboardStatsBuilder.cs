using SoftTime.Application.DTOs;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Dashboard;

/// <summary>Assemble les statistiques du tableau de bord à partir des événements collectés.</summary>
internal static class DashboardStatsBuilder
{
    /// <summary>
    /// Toutes les périodes de la plage figurent, même celles sans événement : les courbes et les
    /// tableaux restent ainsi alignés (une semaine sans absence vaut 0 et n'est pas omise).
    /// </summary>
    public static DashboardStatsDto Build(
        DashboardData data, DashboardEvents events, DashboardKpiDto kpi, string periode)
    {
        var periodes = PeriodGrouping.Clefs(data.From, data.To, periode);
        var cartes = data.Perimetre.Cartes;
        var repartition = new DashboardRepartitionDto(
            DashboardIndicators.Repartition(cartes, c => data.CategorieParCle.GetValueOrDefault(DashboardIndicators.Cle(c.SageMatricule)), events.Absences, events.Retards, data.HsParCle),
            DashboardIndicators.Repartition(cartes, c => c.Departement, events.Absences, events.Retards, data.HsParCle));

        return new DashboardStatsDto(
            cartes.Count,
            kpi,
            BuildAbsences(data, events, periodes, periode),
            BuildRetards(data, events, periodes, periode),
            BuildSeries(data.Semaines, periodes, periode),
            repartition,
            DashboardIndicators.Assiduite(data, events));
    }

    private static DashboardAbsencesDto BuildAbsences(
        DashboardData data, DashboardEvents events, IReadOnlyList<(DateTime Cle, string Libelle)> periodes, string periode)
    {
        var absences = events.Absences;
        var parPeriode = absences
            .GroupBy(a => PeriodGrouping.Grouper(a.Date, periode).Cle)
            .ToDictionary(g => g.Key, g => g.Count());
        var stats = periodes
            .Select(p => new DashboardGroupDto(p.Libelle, parPeriode.GetValueOrDefault(p.Cle), 0))
            .ToList();
        var top = absences
            .GroupBy(a => a.Cle, StringComparer.OrdinalIgnoreCase)
            .Select(g => DashboardIndicators.ToTop(g.Key, g.Count(), 0, data.Cles))
            .OrderByDescending(t => t.Count)
            .Take(5)
            .ToList();
        var explicites = absences.Count(a => data.CorrectionsParCle.TryGetValue(a.Cle, out var l)
            && l.Any(h => h.DatePointageIn?.Date == a.Date && (h.AbsenceAm == true || h.AbsencePm == true)));
        return new DashboardAbsencesDto(absences.Count, explicites, absences.Count - explicites, stats, top);
    }

    private static DashboardRetardsDto BuildRetards(
        DashboardData data, DashboardEvents events, IReadOnlyList<(DateTime Cle, string Libelle)> periodes, string periode)
    {
        var retards = events.Retards;
        var parPeriode = retards
            .GroupBy(r => PeriodGrouping.Grouper(r.Date, periode).Cle)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Minutes: g.Sum(r => r.Minutes)));
        var stats = periodes
            .Select(p => parPeriode.TryGetValue(p.Cle, out var retard)
                ? new DashboardGroupDto(p.Libelle, retard.Count, retard.Minutes)
                : new DashboardGroupDto(p.Libelle, 0, 0))
            .ToList();
        var top = retards
            .GroupBy(r => r.Cle, StringComparer.OrdinalIgnoreCase)
            .Select(g => DashboardIndicators.ToTop(g.Key, g.Count(), g.Sum(r => r.Minutes), data.Cles))
            .OrderByDescending(t => t.TotalMinutes)
            .Take(5)
            .ToList();
        return new DashboardRetardsDto(retards.Count, events.MinutesRetard, stats, top);
    }

    private static List<DashboardSerieDto> BuildSeries(
        List<SemaineHs> semaines, IReadOnlyList<(DateTime Cle, string Libelle)> periodes, string periode)
    {
        var heuresParPeriode = semaines
            .GroupBy(s => PeriodGrouping.Grouper(s.DateDeb, periode).Cle)
            .ToDictionary(g => g.Key, g => (Ht: g.Sum(s => s.Ht), Hs: g.Sum(s => s.Hs)));
        return periodes
            .Select(p => heuresParPeriode.TryGetValue(p.Cle, out var heures)
                ? new DashboardSerieDto(p.Libelle, heures.Ht, heures.Hs)
                : new DashboardSerieDto(p.Libelle, 0m, 0m))
            .ToList();
    }
}
