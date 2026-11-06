using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

public static class PunchCompletenessRules
{

    /// <summary>
    /// 3.1 — Jour-employé dont les pointages ne s'apparient pas (nombre impair), avec la même
    /// définition que le pré-traitement : regroupement <see cref="PunchDayGrouping"/> (planning en
    /// multi-pointage, jour calendaire en mono) puis <see cref="PunchPairingRules"/>. Les jours d'une
    /// semaine validée (sauf <see cref="ClosingRulesOptions.IgnorerSemainesValidees"/> à <c>false</c>)
    /// et les jours portant une correction non <c>AUTO</c> (MANUEL ou héritée) sont ignorés, comme
    /// dans le pré-traitement.
    /// </summary>
    public static IReadOnlyList<ClosingAnomaly> PointagesIncomplets(
        IReadOnlyList<CardPaie> cartes,
        IReadOnlyList<PlanningSalarie> plannings,
        IReadOnlyList<Pointage> pointages,
        IReadOnlyList<HeureCorrigee> corrections,
        IReadOnlyList<HsValidee> semainesValidees,
        DateTime from,
        DateTime to,
        bool multiPointage,
        ClosingRulesOptions? options = null)
        => PointagesIncomplets(cartes, plannings, ClosingPunchIndex.Build(pointages), corrections, semainesValidees,
            from, to, multiPointage, options);

    /// <summary>3.1 sur un index de pointages déjà construit (même résultat que la surcharge par liste).</summary>
    public static IReadOnlyList<ClosingAnomaly> PointagesIncomplets(
        IReadOnlyList<CardPaie> cartes,
        IReadOnlyList<PlanningSalarie> plannings,
        ClosingPunchIndex pointages,
        IReadOnlyList<HeureCorrigee> corrections,
        IReadOnlyList<HsValidee> semainesValidees,
        DateTime from,
        DateTime to,
        bool multiPointage,
        ClosingRulesOptions? options = null)
    {
        options ??= ClosingRulesOptions.Default;
        if (from.Date > to.Date)
            return Array.Empty<ClosingAnomaly>();

        var scope = new PunchCompletenessScope(
            pointages,
            PlanningParCarte(plannings),
            JoursIgnores(corrections, semainesValidees, options),
            from.Date,
            to.Date,
            multiPointage);
        var resultats = ClosingAnomalyFactory.CartesParCle(cartes)
            .SelectMany(c => AnomaliesDeLaCarte(scope, c.Cle, c.Carte))
            .ToList();
        return PayrollClosingRules.Trier(resultats);
    }

    /// <summary>
    /// Jours ignorés : jours portant une correction non AUTO (MANUEL ou héritée) et, selon l'option,
    /// jours des semaines validées.
    /// </summary>
    private static Func<string, DateTime, bool> JoursIgnores(
        IReadOnlyList<HeureCorrigee> corrections, IReadOnlyList<HsValidee> semainesValidees, ClosingRulesOptions options)
    {
        var lundisValides = semainesValidees
            .Where(_ => options.IgnorerSemainesValidees)
            .Where(v => v.ValidationHs == true && v.DateDeb.HasValue && !string.IsNullOrWhiteSpace(v.Matricules))
            .GroupBy(v => MatriculeIdentity.Cle(v.Matricules))
            .ToDictionary(g => g.Key, g => g.Select(v => v.DateDeb!.Value.Date).ToHashSet());
        var joursManuels = corrections
            .Where(h => h.DatePointageIn.HasValue && !string.IsNullOrWhiteSpace(h.MatriculeSage))
            .Where(h => !string.Equals(h.Origine, ClosingAnomalyFactory.OrigineAuto, StringComparison.OrdinalIgnoreCase))
            .Select(h => (Cle: MatriculeIdentity.Cle(h.MatriculeSage), Jour: h.DatePointageIn!.Value.Date))
            .ToHashSet();

        return (cle, jour) =>
        {
            if (joursManuels.Contains((cle, jour)))
                return true;
            var lundi = jour.AddDays(-(((int)jour.DayOfWeek + 6) % 7));
            return lundisValides.TryGetValue(cle, out var lundis) && lundis.Contains(lundi);
        };
    }

    /// <summary>Même planning que le pré-traitement : par carte et par jour, la ligne de plus petit ID.</summary>
    private static Dictionary<int, Dictionary<DateTime, PlanningSalarie>> PlanningParCarte(IReadOnlyList<PlanningSalarie> plannings) =>
        plannings
            .Where(p => p.DateP.HasValue)
            .GroupBy(p => p.IdSal)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(p => p.DateP!.Value.Date)
                    .ToDictionary(gg => gg.Key, gg => gg.OrderBy(p => p.Id).First()));

    private static IEnumerable<ClosingAnomaly> AnomaliesDeLaCarte(PunchCompletenessScope scope, string cle, CardPaie carte)
    {
        var punchs = scope.Pointages.Moments(cle);
        if (punchs.Count == 0)
            yield break;

        var fenetres = scope.MultiPointage
            ? PunchDayGrouping.Fenetres(scope.PlanningParCarte.GetValueOrDefault(carte.Id) ?? new Dictionary<DateTime, PlanningSalarie>())
            : new Dictionary<DateTime, PunchPlanningWindow>();
        foreach (var (jour, moments) in PunchDayGrouping.Build(punchs, fenetres, scope.Debut, scope.Fin))
        {
            if (moments.Count == 0 || scope.EstIgnore(cle, jour))
                continue;
            var resultat = scope.MultiPointage
                ? PunchPairingRules.PairMultipleDay(moments)
                : PunchPairingRules.PairSimpleDay(moments);
            if (resultat.Unpaired.Count > 0 || resultat.Pairs.Count == 0)
                yield return ClosingAnomalyFactory.Anomalie(
                    ClosingAnomalyType.PointageIncomplet, cle, jour,
                    $"{moments.Count} pointage(s), appariement entrée/sortie impossible");
        }
    }

    /// <summary>
    /// 3.2 — Jour planifié avec heures sans aucun pointage ni correction : lignes « Non justifiée »
    /// de <see cref="AbsenceReportRules.Calculer"/> (week-ends, fériés, OFF, congés, Pause et jours
    /// futurs déjà exclus), dont le jour ne porte de surcroît aucun pointage brut.
    /// </summary>
    public static IReadOnlyList<ClosingAnomaly> PointagesManquants(
        IReadOnlyList<CardPaie> cartes,
        IReadOnlyList<PlanningSalarie> plannings,
        IReadOnlyList<Pointage> pointages,
        IReadOnlyList<HeureCorrigee> corrections,
        IReadOnlyDictionary<string, List<LeaveDay>> congesParCle,
        IReadOnlySet<DateTime> feries,
        DateTime from,
        DateTime to,
        DateTime dateDuJour)
        => PointagesManquants(
            AbsenceReportRules.Calculer(plannings, cartes, corrections, congesParCle, feries, from, to, dateDuJour),
            ClosingPunchIndex.Build(pointages));

    /// <summary>
    /// 3.2 sur les lignes déjà calculées par <see cref="AbsenceReportRules.Calculer"/> (partagées avec
    /// 3.3 pour ne les calculer qu'une fois) et un index de pointages.
    /// </summary>
    public static IReadOnlyList<ClosingAnomaly> PointagesManquants(
        IReadOnlyList<AbsenceReportRow> lignesAbsence,
        ClosingPunchIndex pointages)
    {
        var resultats = lignesAbsence
            .Where(r => r.Type == AbsenceReportRules.TypeNonJustifiee)
            .Select(r => (Ligne: r, Cle: MatriculeIdentity.Cle(r.Matricule)))
            .Where(x => !pointages.APointage(x.Cle, x.Ligne.Date))
            .Select(x => ClosingAnomalyFactory.Anomalie(
                ClosingAnomalyType.PointageManquant, x.Cle, x.Ligne.Date,
                "Jour planifié sans pointage ni correction", x.Ligne.DureeHeures))
            .ToList();

        return PayrollClosingRules.Trier(resultats);
    }
}
