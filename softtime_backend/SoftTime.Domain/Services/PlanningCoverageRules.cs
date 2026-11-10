using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

public static class PlanningCoverageRules
{

    /// <summary>
    /// 3.3 — Jour planifié dont une seule moitié est couverte (congé SAGE d'une demi-journée ou
    /// absence arbitrée du matin ou de l'après-midi) et dont l'autre moitié n'a ni pointage ni heure
    /// corrigée. Lignes « Demi-journée », « Matin » ou « Après-midi » de
    /// <see cref="AbsenceReportRules.Calculer"/> avec une durée planifiée ; jamais en même temps
    /// qu'un <see cref="ClosingAnomalyType.PointageManquant"/> (populations disjointes).
    /// </summary>
    public static IReadOnlyList<ClosingAnomaly> PlanningsSansPointage(
        EmployeeSources sources, IReadOnlyList<Pointage> pointages, AbsenceCalendar joursNonTravailles, ReportPeriod periode)
        => PlanningsSansPointage(
            AbsenceReportRules.Calculer(sources, joursNonTravailles, periode), ClosingPunchIndex.Build(pointages), sources.Corrections);

    /// <summary>3.3 sur les lignes déjà calculées par <see cref="AbsenceReportRules.Calculer"/> et un index de pointages.</summary>
    public static IReadOnlyList<ClosingAnomaly> PlanningsSansPointage(
        IReadOnlyList<AbsenceReportRow> lignesAbsence,
        ClosingPunchIndex pointages,
        IReadOnlyList<HeureCorrigee> corrections)
    {
        var correctionsParJour = corrections
            .Where(h => h.DatePointageIn.HasValue && !string.IsNullOrWhiteSpace(h.MatriculeSage))
            .GroupBy(h => (Cle: MatriculeIdentity.Cle(h.MatriculeSage), Jour: h.DatePointageIn!.Value.Date))
            .ToDictionary(g => g.Key, g => g.ToList());
        var resultats = lignesAbsence
            .Select(ligne => DemiJourneeSansPointage(ligne, pointages, correctionsParJour))
            .OfType<ClosingAnomaly>()
            .ToList();
        return PayrollClosingRules.Trier(resultats);
    }

    /// <summary>
    /// Demi-journée de congé ou d'absence arbitrée dont l'autre moitié n'a ni pointage ni heure
    /// corrigée ; <c>null</c> sinon.
    /// </summary>
    private static ClosingAnomaly? DemiJourneeSansPointage(
        AbsenceReportRow ligne,
        ClosingPunchIndex pointages,
        Dictionary<(string Cle, DateTime Jour), List<HeureCorrigee>> correctionsParJour)
    {
        if (ligne.DureeHeures is null)
            return null;
        var demiConge = ligne.Type == AbsenceReportRules.TypeCongeSage && ligne.Periode == "Demi-journée";
        var demiArbitrage = ligne.Type == AbsenceReportRules.TypeArbitreeGta && ligne.Periode is "Matin" or "Après-midi";
        var cle = MatriculeIdentity.Cle(ligne.Matricule);
        var jour = ligne.Date.Date;
        if ((!demiConge && !demiArbitrage) || pointages.APointage(cle, jour))
            return null;

        var corrsDuJour = correctionsParJour.GetValueOrDefault((cle, jour)) ?? new List<HeureCorrigee>();
        // Congé d'une demi-journée : la moitié du congé n'est pas connue (matin ou après-midi),
        // une absence arbitrée le même jour peut donc couvrir l'autre moitié. Prudence : rien.
        if (corrsDuJour.Any(ClosingAnomalyFactory.AHeuresCorrigees)
            || (demiConge && corrsDuJour.Any(h => h.AbsenceAm == true || h.AbsencePm == true)))
            return null;

        return ClosingAnomalyFactory.Anomalie(
            ClosingAnomalyType.PlanningSansPointage, cle, jour,
            demiConge
                ? "Demi-journée de congé, autre moitié sans pointage"
                : "Demi-journée d'absence arbitrée, autre moitié sans pointage",
            ligne.DureeHeures);
    }

    /// <summary>
    /// 3.4 — Jour pointé pour lequel aucune ligne de planning (quelle qu'elle soit, OFF et Pause
    /// compris) n'existe sur les cartes de ce matricule. Seuls les matricules connus des cartes
    /// sont contrôlés.
    /// </summary>
    public static IReadOnlyList<ClosingAnomaly> PointagesSansPlanning(
        IReadOnlyList<CardPaie> cartes, IReadOnlyList<PlanningSalarie> plannings, IReadOnlyList<Pointage> pointages, ClosingScope scope)
    {
        return PointagesSansPlanning(cartes, plannings, ClosingPunchIndex.Build(pointages), scope);
    }

    /// <summary>3.4 sur un index de pointages déjà construit.</summary>
    public static IReadOnlyList<ClosingAnomaly> PointagesSansPlanning(
        IReadOnlyList<CardPaie> cartes, IReadOnlyList<PlanningSalarie> plannings, ClosingPunchIndex pointages, ClosingScope scope)
    {
        var (from, to, _, options) = scope;
        options ??= ClosingRulesOptions.Default;
        var debut = from.Date;
        var fin = to.Date;
        var cleParIdSal = ClosingAnomalyFactory.CleParIdSal(cartes);
        var exclus = options.ExclureSansPlanningSurPeriode
            ? SalariesSansPlanningSurPeriode(cartes, plannings, pointages, new ClosingScope(from, to))
            : new HashSet<string>(StringComparer.Ordinal);

        var joursPlanifies = plannings
            .Where(p => p.DateP.HasValue && cleParIdSal.ContainsKey(p.IdSal))
            .Select(p => (Cle: cleParIdSal[p.IdSal], Jour: p.DateP!.Value.Date))
            .ToHashSet();
        var clesConnues = cleParIdSal.Values.ToHashSet();

        var resultats = pointages.Jours
            .Where(x => x.Jour >= debut && x.Jour <= fin && clesConnues.Contains(x.Cle))
            .Where(x => !exclus.Contains(x.Cle))
            .Where(x => !joursPlanifies.Contains((x.Cle, x.Jour)))
            .Select(x => ClosingAnomalyFactory.Anomalie(
                ClosingAnomalyType.PointageSansPlanning, x.Cle, x.Jour,
                $"{x.Nombre} pointage(s) sans ligne de planning"))
            .ToList();

        return PayrollClosingRules.Trier(resultats);
    }

    /// <summary>
    /// Salariés (clés) ayant au moins un pointage sur la période mais AUCUNE ligne de planning, de toute
    /// nature (travail, OFF, Pause), sur cette même période : ils ne sont pas contrôlés par la règle
    /// « pointage sans planning » quand <see cref="ClosingRulesOptions.ExclureSansPlanningSurPeriode"/>
    /// est vrai. Un salarié partiellement planifié n'en fait pas partie.
    /// </summary>
    public static IReadOnlySet<string> SalariesSansPlanningSurPeriode(
        IReadOnlyList<CardPaie> cartes, IReadOnlyList<PlanningSalarie> plannings, ClosingPunchIndex pointages, ClosingScope scope)
    {
        var (from, to, _, _) = scope;
        var debut = from.Date;
        var fin = to.Date;
        var cleParIdSal = ClosingAnomalyFactory.CleParIdSal(cartes);

        var planifies = plannings
            .Where(p => p.DateP.HasValue && cleParIdSal.ContainsKey(p.IdSal))
            .Where(p => p.DateP!.Value.Date >= debut && p.DateP!.Value.Date <= fin)
            .Select(p => cleParIdSal[p.IdSal])
            .ToHashSet(StringComparer.Ordinal);
        var clesConnues = cleParIdSal.Values.ToHashSet(StringComparer.Ordinal);

        return pointages.Jours
            .Where(x => x.Jour >= debut && x.Jour <= fin && clesConnues.Contains(x.Cle) && !planifies.Contains(x.Cle))
            .Select(x => x.Cle)
            .ToHashSet(StringComparer.Ordinal);
    }
}
