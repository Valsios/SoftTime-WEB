using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Domain.Services;

/// <summary>
/// Ligne de l'état des retards : un salarié, un jour, l'heure d'entrée retenue, l'heure prévue
/// au planning (avec la tolérance de sa catégorie) et le retard calculé en minutes.
/// </summary>
public sealed record RetardReportRow(
    string Matricule,
    string? Nom,
    string? Prenom,
    string? Departement,
    DateTime Date,
    TimeSpan? HeureEntree,
    TimeSpan? HeurePrevue,
    int ToleranceMinutes,
    int MinutesRetard,
    bool RetardSaisi
);

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
    public static TimeSpan? HeurePrevue(T_PLANNING_SAL? plan)
    {
        if (plan is null || plan.OffP == true)
            return null;
        if (plan.HA is not DateTime ha || ha.Date <= SentinelleHeure)
            return null;
        if (AbsenceReportRules.CalculerDureeHeures(plan) is null)
            return null;

        return ha.TimeOfDay;
    }

    /// <summary>Tolérance d'entrée de la catégorie du salarié (<c>TYPES_TOL = false</c>), en minutes.</summary>
    public static int ToleranceEntree(
        int carteId,
        IReadOnlyDictionary<int, int> categorieParCarte,
        IReadOnlyList<T_TOLERANCE> tolerances)
    {
        if (!categorieParCarte.TryGetValue(carteId, out var idCategorie))
            return 0;

        return tolerances
            .FirstOrDefault(t => t.IDCATEGORIE == idCategorie && t.TYPES_TOL == false)?.TOLERANCE ?? 0;
    }

    public static IReadOnlyList<RetardReportRow> Calculer(
        IReadOnlyList<T_CARDPAIE> cartes,
        IReadOnlyList<T_HEURECORRIGER> corrections,
        IReadOnlyList<T_PLANNING_SAL> plannings,
        IReadOnlyDictionary<int, int> categorieParCarte,
        IReadOnlyList<T_TOLERANCE> tolerances,
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

        var cartesParCle = new Dictionary<string, T_CARDPAIE>(StringComparer.OrdinalIgnoreCase);
        var cleParIdSal = new Dictionary<int, string>();
        foreach (var carte in cartes)
        {
            var cle = MatriculeIdentity.Cle(carte.SAGE_MATRICULE);
            if (string.IsNullOrEmpty(cle))
                continue;

            cleParIdSal[carte.ID] = cle;
            if (!cartesParCle.TryGetValue(cle, out var existante) || carte.ID < existante.ID)
                cartesParCle[cle] = carte;
        }

        if (cartesParCle.Count == 0)
            return Array.Empty<RetardReportRow>();

        // Planning du jour travaillé, par clé matricule et par date (plus petite ligne retenue).
        var planningsParCleEtDate = new Dictionary<(string Cle, DateTime Date), T_PLANNING_SAL>();
        foreach (var plan in plannings)
        {
            if (plan.DateP is not DateTime dateP || plan.OffP == true)
                continue;
            if (!cleParIdSal.TryGetValue(plan.IDSAL, out var cle))
                continue;

            var jour = dateP.Date;
            if (jour < debut || jour > fin)
                continue;

            var key = (cle, jour);
            if (!planningsParCleEtDate.TryGetValue(key, out var existant) || plan.ID < existant.ID)
                planningsParCleEtDate[key] = plan;
        }

        var correctionsParCle = corrections
            .Where(h => h.DATE_POINTAGE_IN.HasValue)
            .GroupBy(h => MatriculeIdentity.Cle(h.MATRICULE_SAGE), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var resultats = new List<RetardReportRow>();

        foreach (var (cle, carte) in cartesParCle)
        {
            if (!correctionsParCle.TryGetValue(cle, out var lignes) || lignes.Count == 0)
                continue;

            var matriculeAffiche = !string.IsNullOrWhiteSpace(carte.SAGE_MATRICULE) ? carte.SAGE_MATRICULE.Trim() : cle;
            var nomAffiche = carte.SAGE_NOM?.Trim();
            var prenomAffiche = carte.SAGE_PRENOM?.Trim();
            var deptAffiche = carte.DEPARTEMENT?.Trim();
            var tolerance = ToleranceEntree(carte.ID, categorieParCarte, tolerances);

            foreach (var groupeJour in lignes
                         .Where(h => h.DATE_POINTAGE_IN.HasValue)
                         .GroupBy(h => h.DATE_POINTAGE_IN!.Value.Date))
            {
                var jour = groupeJour.Key;
                if (jour < debut || jour > fin || AbsenceReportRules.EstNonTravaille(jour, feries))
                    continue;

                planningsParCleEtDate.TryGetValue((cle, jour), out var plan);
                var heurePrevue = HeurePrevue(plan);
                var entrees = groupeJour
                    .Where(h => h.HENTREE.HasValue)
                    .Select(h => h.HENTREE!.Value)
                    .ToList();
                TimeSpan? premiereEntree = entrees.Count > 0 ? entrees.Min() : null;
                var retardsSaisis = groupeJour.Select(h => h.RETARD).ToList();

                var minutes = MinutesRetardJour(
                    absenceMatin: groupeJour.Any(h => h.ABSENCE_AM == true),
                    retardsSaisis: retardsSaisis,
                    premiereEntree: premiereEntree,
                    heurePrevue: heurePrevue,
                    toleranceMinutes: tolerance);

                if (minutes <= 0)
                    continue;

                resultats.Add(new RetardReportRow(
                    Matricule: matriculeAffiche,
                    Nom: nomAffiche,
                    Prenom: prenomAffiche,
                    Departement: deptAffiche,
                    Date: jour,
                    HeureEntree: premiereEntree,
                    HeurePrevue: heurePrevue,
                    ToleranceMinutes: tolerance,
                    MinutesRetard: minutes,
                    RetardSaisi: retardsSaisis.Any(r => r.HasValue)
                ));
            }
        }

        return resultats
            .OrderBy(r => r.Date)
            .ThenBy(r => r.Matricule, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
