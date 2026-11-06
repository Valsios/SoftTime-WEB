using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Domain.Services;

/// <summary>
/// Règles pures de calcul de l'état des retards et des composants partagés avec le tableau de bord.
///
/// Portage de la logique d'affichage de <c>Frm_EtatRETARD</c> (planning + tolérance de catégorie),
/// avec les écarts assumés suivants :
/// <list type="bullet">
///   <item>seul le <b>début de journée</b> est contrôlé (première entrée du jour) : la branche
///     après-midi du desktop (<c>HPE</c> vs 13:30) n'est pas portée ;</item>
///   <item>sans planning ni heure prévue, aucun retard n'est calculé (le desktop retombait sur
///     00:00 et comptait l'heure d'entrée entière) ;</item>
///   <item>le retard est <b>jamais négatif</b> ;</item>
///   <item>un jour dont l'absence du matin est déclarée (<c>ABSENCE_AM</c>) n'est jamais en retard :
///     l'arrivée de l'après-midi ne doit pas être comparée à l'heure du matin ;</item>
///   <item>la valeur <c>RETARD</c> saisie sur la correction reste prioritaire, comme dans le
///     tableau de bord.</item>
/// </list>
/// Mono et multi-pointage donnent le même résultat : les corrections sont regroupées par
/// <c>DATE_POINTAGE_IN</c> et seule la première entrée du jour compte.
/// </summary>
public static class RetardReportRules
{
    private static readonly DateTime SentinelleHeure = new(2000, 1, 1);

    /// <summary>
    /// Retard en minutes : <c>entrée − (heure prévue + tolérance)</c>, borné à 0. Les minutes sont
    /// tronquées (jamais arrondies au supérieur) comme dans le tableau de bord.
    /// </summary>
    public static int MinutesRetard(TimeSpan entree, TimeSpan heurePrevue, int toleranceMinutes)
    {
        var tolerance = TimeSpan.FromMinutes(Math.Max(0, toleranceMinutes));
        var retard = entree - (heurePrevue + tolerance);
        return retard > TimeSpan.Zero ? (int)retard.TotalMinutes : 0;
    }

    /// <summary>
    /// Règle « un jour = une ligne », partagée par l'état et le tableau de bord : une absence du
    /// matin annule le retard, une valeur saisie est prioritaire, sinon on compare la première
    /// entrée du jour à l'heure planifiée.
    /// </summary>
    public static int MinutesRetardJour(
        bool absenceMatin,
        IEnumerable<TimeSpan?> retardsSaisis,
        TimeSpan? premiereEntree,
        TimeSpan? heurePrevue,
        int toleranceMinutes)
    {
        if (absenceMatin)
            return 0;

        var saisis = retardsSaisis
            .Where(r => r.HasValue)
            .Select(r => (int)Math.Max(0m, (decimal)r!.Value.TotalMinutes))
            .ToList();
        if (saisis.Count > 0)
            return saisis.Max();

        if (!premiereEntree.HasValue || !heurePrevue.HasValue)
            return 0;

        return MinutesRetard(premiereEntree.Value, heurePrevue.Value, toleranceMinutes);
    }

    /// <summary>
    /// Heure d'entrée planifiée du jour, ou <c>null</c> quand le jour n'est pas travaillé :
    /// planning OFF, heure absente, date sentinelle héritée du desktop ou journée sans heures
    /// exploitables (shift Pause, HA = HD). Même notion de « jour travaillé » que l'état des
    /// absences, pour que les deux états parlent des mêmes journées.
    /// </summary>
    public static TimeSpan? HeurePrevue(PlanningSalarie? plan)
    {
        if (plan is null || plan.OffP == true)
            return null;
        if (plan.Ha is not DateTime ha || ha.Date <= SentinelleHeure)
            return null;
        if (AbsenceReportRules.CalculerDureeHeures(plan) is null)
            return null;

        return ha.TimeOfDay;
    }

    /// <summary>Tolérance d'entrée de la catégorie du salarié (<c>TYPES_TOL = false</c>), en minutes.</summary>
    public static int ToleranceEntree(
        int carteId,
        IReadOnlyDictionary<int, int> categorieParCarte,
        IReadOnlyList<ToleranceRetard> tolerances)
    {
        if (!categorieParCarte.TryGetValue(carteId, out var idCategorie))
            return 0;

        return tolerances
            .FirstOrDefault(t => t.IdCategorie == idCategorie && t.TypesTol == false)?.Tolerance ?? 0;
    }

    public static IReadOnlyList<RetardReportRow> Calculer(
        IReadOnlyList<CardPaie> cartes,
        IReadOnlyList<HeureCorrigee> corrections,
        IReadOnlyList<PlanningSalarie> plannings,
        IReadOnlyDictionary<int, int> categorieParCarte,
        IReadOnlyList<ToleranceRetard> tolerances,
        IReadOnlySet<DateTime> feries,
        DateTime from,
        DateTime to,
        DateTime dateDuJour)
    {
        var debut = from.Date;
        var hier = dateDuJour.Date.AddDays(-1);

        // Aucun jour futur : la période demandée est plafonnée à la veille.
        var fin = to.Date < hier ? to.Date : hier;
        if (debut > fin)
            return Array.Empty<RetardReportRow>();

        var (cleParIdSal, cartesParCle) = AbsenceReportRules.IndexerCartes(cartes);
        if (cartesParCle.Count == 0)
            return Array.Empty<RetardReportRow>();

        // Planning du jour travaillé, par clé matricule et par date (plus petite ligne retenue).
        var planningsTravailles = plannings.Where(p => p.OffP != true).ToList();
        var calendrier = new ReportCalendar(
            debut, fin, feries, AbsenceReportRules.IndexerPlannings(planningsTravailles, cleParIdSal, debut, fin));
        var correctionsParCle = corrections
            .Where(h => h.DatePointageIn.HasValue)
            .GroupBy(h => MatriculeIdentity.Cle(h.MatriculeSage), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        return cartesParCle
            .Where(kv => correctionsParCle.TryGetValue(kv.Key, out var lignes) && lignes.Count > 0)
            .SelectMany(kv => new RetardReportEmployee(
                    kv.Key,
                    kv.Value,
                    correctionsParCle[kv.Key],
                    ToleranceEntree(kv.Value.Id, categorieParCarte, tolerances))
                .Retards(calendrier))
            .OrderBy(r => r.Date)
            .ThenBy(r => r.Matricule, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
