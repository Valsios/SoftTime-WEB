using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

public static class HolidayPunchRules
{

    /// <summary>
    /// 3.7 — Jour férié où le salarié est planifié avec heures (ni OFF ni Pause) et où aucune
    /// correction du jour ne porte le férié (<c>FERIER</c>, <c>FERIER_AM</c>, <c>FERIER_PM</c> ou
    /// <c>M_FERIES</c> &gt; 0). Planning retenu comme dans <see cref="AbsenceReportRules"/> : une
    /// ligne de travail prime sur une ligne OFF, puis le plus petit ID. Aucun jour futur.
    /// </summary>
    public static IReadOnlyList<ClosingAnomaly> FeriesNonAppliques(
        EmployeeSources sources, IReadOnlySet<DateTime> feries, ReportPeriod periode, ClosingPunchIndex? pointages = null)
    {
        var (cartes, plannings, corrections) = sources;
        var (debut, fin) = (periode.Debut, periode.Fin);
        if (periode.EstVide)
            return Array.Empty<ClosingAnomaly>();

        // Même planning retenu que l'état des absences (travail prioritaire sur OFF), jours fériés seulement.
        var planningsFeries = plannings.Where(p => p.DateP is DateTime d && feries.Contains(d.Date)).ToList();
        var planningRetenu = AbsenceReportRules.IndexerPlannings(
            planningsFeries, ClosingAnomalyFactory.CleParIdSal(cartes), debut, fin);
        var joursFeriesAppliques = corrections
            .Where(h => h.DatePointageIn.HasValue && !string.IsNullOrWhiteSpace(h.MatriculeSage))
            .Where(h => h.Ferier == true || h.FerierAm == true || h.FerierPm == true || h.MFeries > 0)
            .Select(h => (Cle: MatriculeIdentity.Cle(h.MatriculeSage), Jour: h.DatePointageIn!.Value.Date))
            .ToHashSet();

        var resultats = planningRetenu
            .Where(kv => !AbsenceReportRules.EstJourOff(kv.Value) && !joursFeriesAppliques.Contains(kv.Key))
            .Select(kv => (kv.Key, Duree: AbsenceReportRules.CalculerDureeHeures(kv.Value)))
            .Where(x => x.Duree is not null)
            .Select(x => ClosingAnomalyFactory.Anomalie(
                ClosingAnomalyType.FerieNonApplique, x.Key.Cle, x.Key.Date,
                "drapeau férié absent (sans effet sur le calcul de la paie)", x.Duree,
                pointageExiste: pointages?.APointage(x.Key.Cle, x.Key.Date)))
            .ToList();
        return PayrollClosingRules.Trier(resultats);
    }
}
