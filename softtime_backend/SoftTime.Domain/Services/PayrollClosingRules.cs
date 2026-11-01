using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

/// <summary>
/// Règles pures de contrôle avant clôture de paie : chaque règle prend des listes déjà chargées et
/// renvoie des <see cref="ClosingAnomaly"/> triées de façon déterministe (date, matricule, type,
/// détail). Aucun accès base, aucune configuration lue.
///
/// Les règles existantes sont réutilisées plutôt que recodées :
/// <list type="bullet">
///   <item><see cref="AbsenceReportRules"/> pour les jours planifiés sans pointage (sélection du
///     planning, week-ends, fériés, OFF, congés, jours futurs) et la durée planifiée ;</item>
///   <item><see cref="PunchDayGrouping"/> et <see cref="PunchPairingRules"/> pour la parité des
///     pointages, comme le pré-traitement des pointages ;</item>
///   <item><see cref="MatriculeIdentity.Cle"/> pour identifier un salarié.</item>
/// </list>
///
/// Invariant « Pause » : une ligne de planning sans heures (HA = HD, shift Pause) n'est jamais une
/// absence et ne génère jamais d'anomalie de planning (<see cref="AbsenceReportRules.CalculerDureeHeures"/>
/// renvoie <c>null</c>).
/// </summary>
public static class PayrollClosingRules
{
    /// <summary>
    /// Formule du score de clôture, réutilisable telle quelle par l'écran :
    /// <c>100 − 100 × Σ poids ÷ (poids maximum × jours-employé contrôlés)</c>, bornée à [0, 100] et
    /// arrondie à l'entier ; aucun score quand aucun jour-employé n'est contrôlé.
    /// </summary>
    public const string FormuleScore =
        "Score = 100 − 100 × (somme des poids des anomalies) ÷ (poids maximum × jours-employé contrôlés), "
        + "borné entre 0 et 100 et arrondi à l'entier. Sans jour-employé contrôlé, aucun score n'est calculé.";

    /// <summary>Origine des lignes régénérées par le pré-traitement (miroir de <c>PunchProcessingConfiguration.OrigineAuto</c>).</summary>
    private const string OrigineAuto = "AUTO";

    /// <summary>Gravité par défaut de chaque type d'anomalie.</summary>
    public static ClosingSeverity GraviteParDefaut(ClosingAnomalyType type) => type switch
    {
        ClosingAnomalyType.PointageIncomplet => ClosingSeverity.Critique,
        ClosingAnomalyType.PointageManquant => ClosingSeverity.Critique,
        ClosingAnomalyType.PlanningSansPointage => ClosingSeverity.Importante,
        ClosingAnomalyType.PointageSansPlanning => ClosingSeverity.Importante,
        ClosingAnomalyType.FerieNonApplique => ClosingSeverity.Importante,
        ClosingAnomalyType.DoublonExact => ClosingSeverity.Mineure,
        ClosingAnomalyType.QuasiDoublon => ClosingSeverity.Mineure,
        _ => ClosingSeverity.Critique
    };

    /// <summary>
    /// 3.1 — Jour-employé dont les pointages ne s'apparient pas (nombre impair), avec la même
    /// définition que le pré-traitement : regroupement <see cref="PunchDayGrouping"/> (planning en
    /// multi-pointage, jour calendaire en mono) puis <see cref="PunchPairingRules"/>. Les jours d'une
    /// semaine validée (sauf <see cref="ClosingRulesOptions.IgnorerSemainesValidees"/> à <c>false</c>)
    /// et les jours portant une correction non <c>AUTO</c> (MANUEL ou héritée) sont ignorés, comme
    /// dans le pré-traitement.
    /// </summary>
    public static IReadOnlyList<ClosingAnomaly> PointagesIncomplets(
        IReadOnlyList<T_CARDPAIE> cartes,
        IReadOnlyList<T_PLANNING_SAL> plannings,
        IReadOnlyList<T_POINTAGE> pointages,
        IReadOnlyList<T_HEURECORRIGER> corrections,
        IReadOnlyList<T_HS_VALIDER> semainesValidees,
        DateTime from,
        DateTime to,
        bool multiPointage,
        ClosingRulesOptions? options = null)
    {
        options ??= ClosingRulesOptions.Default;
        var debut = from.Date;
        var fin = to.Date;
        if (debut > fin)
            return Array.Empty<ClosingAnomaly>();

        var lundisValides = semainesValidees
            .Where(_ => options.IgnorerSemainesValidees)
            .Where(v => v.VALIDATION_HS == true && v.DATE_DEB.HasValue && !string.IsNullOrWhiteSpace(v.MATRICULES))
            .GroupBy(v => MatriculeIdentity.Cle(v.MATRICULES))
            .ToDictionary(g => g.Key, g => g.Select(v => v.DATE_DEB!.Value.Date).ToHashSet());

        var joursManuels = corrections
            .Where(h => h.DATE_POINTAGE_IN.HasValue && !string.IsNullOrWhiteSpace(h.MATRICULE_SAGE))
            .Where(h => !string.Equals(h.ORIGINE, OrigineAuto, StringComparison.OrdinalIgnoreCase))
            .Select(h => (Cle: MatriculeIdentity.Cle(h.MATRICULE_SAGE), Jour: h.DATE_POINTAGE_IN!.Value.Date))
            .ToHashSet();

        bool EstIgnore(string cle, DateTime jour)
        {
            if (joursManuels.Contains((cle, jour)))
                return true;
            var lundi = jour.AddDays(-(((int)jour.DayOfWeek + 6) % 7));
            return lundisValides.TryGetValue(cle, out var lundis) && lundis.Contains(lundi);
        }

        var momentsParCle = PointagesValides(pointages)
            .GroupBy(p => MatriculeIdentity.Cle(p.MATRICULE_SAGE))
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<PunchMoment>)g
                    .Select(p => new PunchMoment(p.DATE_POINTAGE!.Value.Date, p.HEURE_POINTAGE!.Value))
                    .ToList());

        // Même planning que le pré-traitement : par carte et par jour, la ligne de plus petit ID.
        var planningParCarte = plannings
            .Where(p => p.DateP.HasValue)
            .GroupBy(p => p.IDSAL)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(p => p.DateP!.Value.Date)
                    .ToDictionary(gg => gg.Key, gg => gg.OrderBy(p => p.ID).First()));

        var resultats = new List<ClosingAnomaly>();
        foreach (var (cle, carte) in CartesParCle(cartes))
        {
            if (!momentsParCle.TryGetValue(cle, out var punchs))
                continue;

            var planningCarte = planningParCarte.GetValueOrDefault(carte.ID)
                                ?? new Dictionary<DateTime, T_PLANNING_SAL>();
            var fenetres = multiPointage
                ? planningCarte
                    .Where(kv => kv.Value.OffP != true)
                    .ToDictionary(
                        kv => kv.Key,
                        kv => new PunchPlanningWindow(kv.Key, kv.Value.HA, kv.Value.HD, kv.Value.OffP == true))
                : new Dictionary<DateTime, PunchPlanningWindow>();
            var momentsParJour = PunchDayGrouping.Build(punchs, fenetres, debut, fin);

            foreach (var (jour, moments) in momentsParJour)
            {
                if (moments.Count == 0 || EstIgnore(cle, jour))
                    continue;

                var resultat = multiPointage
                    ? PunchPairingRules.PairMultipleDay(moments)
                    : PunchPairingRules.PairSimpleDay(moments);
                if (resultat.Unpaired.Count == 0 && resultat.Pairs.Count > 0)
                    continue;

                resultats.Add(Anomalie(
                    ClosingAnomalyType.PointageIncomplet, cle, jour,
                    $"{moments.Count} pointage(s), appariement entrée/sortie impossible"));
            }
        }

        return Trier(resultats);
    }

    /// <summary>
    /// 3.2 — Jour planifié avec heures sans aucun pointage ni correction : lignes « Non justifiée »
    /// de <see cref="AbsenceReportRules.Calculer"/> (week-ends, fériés, OFF, congés, Pause et jours
    /// futurs déjà exclus), dont le jour ne porte de surcroît aucun pointage brut.
    /// </summary>
    public static IReadOnlyList<ClosingAnomaly> PointagesManquants(
        IReadOnlyList<T_CARDPAIE> cartes,
        IReadOnlyList<T_PLANNING_SAL> plannings,
        IReadOnlyList<T_POINTAGE> pointages,
        IReadOnlyList<T_HEURECORRIGER> corrections,
        IReadOnlyDictionary<string, List<LeaveDay>> congesParCle,
        IReadOnlySet<DateTime> feries,
        DateTime from,
        DateTime to,
        DateTime dateDuJour)
    {
        var joursPointes = JoursPointes(pointages);

        var resultats = AbsenceReportRules
            .Calculer(plannings, cartes, corrections, congesParCle, feries, from, to, dateDuJour)
            .Where(r => r.Type == AbsenceReportRules.TypeNonJustifiee)
            .Select(r => (Ligne: r, Cle: MatriculeIdentity.Cle(r.Matricule)))
            .Where(x => !joursPointes.Contains((x.Cle, x.Ligne.Date.Date)))
            .Select(x => Anomalie(
                ClosingAnomalyType.PointageManquant, x.Cle, x.Ligne.Date,
                "Jour planifié sans pointage ni correction", x.Ligne.DureeHeures))
            .ToList();

        return Trier(resultats);
    }

    /// <summary>
    /// 3.3 — Jour planifié dont une seule moitié est couverte (congé SAGE d'une demi-journée ou
    /// absence arbitrée du matin ou de l'après-midi) et dont l'autre moitié n'a ni pointage ni heure
    /// corrigée. Lignes « Demi-journée », « Matin » ou « Après-midi » de
    /// <see cref="AbsenceReportRules.Calculer"/> avec une durée planifiée ; jamais en même temps
    /// qu'un <see cref="ClosingAnomalyType.PointageManquant"/> (populations disjointes).
    /// </summary>
    public static IReadOnlyList<ClosingAnomaly> PlanningsSansPointage(
        IReadOnlyList<T_CARDPAIE> cartes,
        IReadOnlyList<T_PLANNING_SAL> plannings,
        IReadOnlyList<T_POINTAGE> pointages,
        IReadOnlyList<T_HEURECORRIGER> corrections,
        IReadOnlyDictionary<string, List<LeaveDay>> congesParCle,
        IReadOnlySet<DateTime> feries,
        DateTime from,
        DateTime to,
        DateTime dateDuJour)
    {
        var joursPointes = JoursPointes(pointages);
        var correctionsParJour = corrections
            .Where(h => h.DATE_POINTAGE_IN.HasValue && !string.IsNullOrWhiteSpace(h.MATRICULE_SAGE))
            .GroupBy(h => (Cle: MatriculeIdentity.Cle(h.MATRICULE_SAGE), Jour: h.DATE_POINTAGE_IN!.Value.Date))
            .ToDictionary(g => g.Key, g => g.ToList());

        var resultats = new List<ClosingAnomaly>();
        foreach (var ligne in AbsenceReportRules.Calculer(plannings, cartes, corrections, congesParCle, feries, from, to, dateDuJour))
        {
            if (ligne.DureeHeures is null)
                continue;

            var demiConge = ligne.Type == AbsenceReportRules.TypeCongeSage && ligne.Periode == "Demi-journée";
            var demiArbitrage = ligne.Type == AbsenceReportRules.TypeArbitreeGta
                                && ligne.Periode is "Matin" or "Après-midi";
            if (!demiConge && !demiArbitrage)
                continue;

            var cle = MatriculeIdentity.Cle(ligne.Matricule);
            var jour = ligne.Date.Date;
            if (joursPointes.Contains((cle, jour)))
                continue;

            var corrsDuJour = correctionsParJour.GetValueOrDefault((cle, jour)) ?? new List<T_HEURECORRIGER>();
            if (corrsDuJour.Any(AHeuresCorrigees))
                continue;

            // Congé d'une demi-journée : la moitié du congé n'est pas connue (matin ou après-midi),
            // une absence arbitrée le même jour peut donc couvrir l'autre moitié. Prudence : rien.
            if (demiConge && corrsDuJour.Any(h => h.ABSENCE_AM == true || h.ABSENCE_PM == true))
                continue;

            resultats.Add(Anomalie(
                ClosingAnomalyType.PlanningSansPointage, cle, jour,
                demiConge
                    ? "Demi-journée de congé, autre moitié sans pointage"
                    : "Demi-journée d'absence arbitrée, autre moitié sans pointage",
                ligne.DureeHeures));
        }

        return Trier(resultats);
    }

    /// <summary>
    /// 3.4 — Jour pointé pour lequel aucune ligne de planning (quelle qu'elle soit, OFF et Pause
    /// compris) n'existe sur les cartes de ce matricule. Seuls les matricules connus des cartes
    /// sont contrôlés.
    /// </summary>
    public static IReadOnlyList<ClosingAnomaly> PointagesSansPlanning(
        IReadOnlyList<T_CARDPAIE> cartes,
        IReadOnlyList<T_PLANNING_SAL> plannings,
        IReadOnlyList<T_POINTAGE> pointages,
        DateTime from,
        DateTime to)
    {
        var debut = from.Date;
        var fin = to.Date;
        var cleParIdSal = CleParIdSal(cartes);

        var joursPlanifies = plannings
            .Where(p => p.DateP.HasValue && cleParIdSal.ContainsKey(p.IDSAL))
            .Select(p => (Cle: cleParIdSal[p.IDSAL], Jour: p.DateP!.Value.Date))
            .ToHashSet();
        var clesConnues = cleParIdSal.Values.ToHashSet();

        var resultats = PointagesValides(pointages)
            .Select(p => (Cle: MatriculeIdentity.Cle(p.MATRICULE_SAGE), Jour: p.DATE_POINTAGE!.Value.Date))
            .Where(x => x.Jour >= debut && x.Jour <= fin && clesConnues.Contains(x.Cle))
            .GroupBy(x => x)
            .Where(g => !joursPlanifies.Contains(g.Key))
            .Select(g => Anomalie(
                ClosingAnomalyType.PointageSansPlanning, g.Key.Cle, g.Key.Jour,
                $"{g.Count()} pointage(s) sans ligne de planning"))
            .ToList();

        return Trier(resultats);
    }

    /// <summary>3.5 — Plusieurs pointages du même matricule à la même date et à la même heure exacte.</summary>
    public static IReadOnlyList<ClosingAnomaly> DoublonsExacts(
        IReadOnlyList<T_POINTAGE> pointages,
        DateTime from,
        DateTime to)
    {
        var debut = from.Date;
        var fin = to.Date;

        var resultats = PointagesValides(pointages)
            .Where(p => p.DATE_POINTAGE!.Value.Date >= debut && p.DATE_POINTAGE!.Value.Date <= fin)
            .GroupBy(p => (Cle: MatriculeIdentity.Cle(p.MATRICULE_SAGE), Jour: p.DATE_POINTAGE!.Value.Date, Heure: p.HEURE_POINTAGE!.Value))
            .Where(g => g.Count() > 1)
            .Select(g => Anomalie(
                ClosingAnomalyType.DoublonExact, g.Key.Cle, g.Key.Jour,
                $"{g.Count()} pointages identiques à {FormatHeure(g.Key.Heure)}"))
            .ToList();

        return Trier(resultats);
    }

    /// <summary>
    /// 3.6 — Deux pointages successifs du même matricule séparés de strictement moins de
    /// <see cref="ClosingRulesOptions.DuplicateToleranceSeconds"/>. Les doublons exacts sont
    /// réduits à un seul instant avant la comparaison : ils ne sont jamais comptés deux fois.
    /// L'anomalie est datée du premier pointage de la paire.
    /// </summary>
    public static IReadOnlyList<ClosingAnomaly> QuasiDoublons(
        IReadOnlyList<T_POINTAGE> pointages,
        DateTime from,
        DateTime to,
        ClosingRulesOptions? options = null)
    {
        options ??= ClosingRulesOptions.Default;
        var tolerance = TimeSpan.FromSeconds(Math.Max(0, options.DuplicateToleranceSeconds));
        var debut = from.Date;
        var fin = to.Date;

        var resultats = new List<ClosingAnomaly>();
        var instantsParCle = PointagesValides(pointages)
            .Where(p => p.DATE_POINTAGE!.Value.Date >= debut && p.DATE_POINTAGE!.Value.Date <= fin)
            .GroupBy(p => MatriculeIdentity.Cle(p.MATRICULE_SAGE));

        foreach (var groupe in instantsParCle)
        {
            var instants = groupe
                .Select(p => p.DATE_POINTAGE!.Value.Date + p.HEURE_POINTAGE!.Value)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            for (var i = 1; i < instants.Count; i++)
            {
                var ecart = instants[i] - instants[i - 1];
                if (ecart >= tolerance)
                    continue;

                resultats.Add(Anomalie(
                    ClosingAnomalyType.QuasiDoublon, groupe.Key, instants[i - 1].Date,
                    $"Écart de {(int)ecart.TotalSeconds} s entre {FormatHeure(instants[i - 1].TimeOfDay)} et {FormatHeure(instants[i].TimeOfDay)}"));
            }
        }

        return Trier(resultats);
    }

    /// <summary>
    /// 3.7 — Jour férié où le salarié est planifié avec heures (ni OFF ni Pause) et où aucune
    /// correction du jour ne porte le férié (<c>FERIER</c>, <c>FERIER_AM</c>, <c>FERIER_PM</c> ou
    /// <c>M_FERIES</c> &gt; 0). Planning retenu comme dans <see cref="AbsenceReportRules"/> : une
    /// ligne de travail prime sur une ligne OFF, puis le plus petit ID. Aucun jour futur.
    /// </summary>
    public static IReadOnlyList<ClosingAnomaly> FeriesNonAppliques(
        IReadOnlyList<T_CARDPAIE> cartes,
        IReadOnlyList<T_PLANNING_SAL> plannings,
        IReadOnlyList<T_HEURECORRIGER> corrections,
        IReadOnlySet<DateTime> feries,
        DateTime from,
        DateTime to,
        DateTime dateDuJour)
    {
        var debut = from.Date;
        var hier = dateDuJour.Date.AddDays(-1);
        var fin = to.Date < hier ? to.Date : hier;
        if (debut > fin)
            return Array.Empty<ClosingAnomaly>();

        var cleParIdSal = CleParIdSal(cartes);

        var planningRetenu = new Dictionary<(string Cle, DateTime Jour), T_PLANNING_SAL>();
        foreach (var plan in plannings)
        {
            if (plan.DateP is not DateTime dateP || !cleParIdSal.TryGetValue(plan.IDSAL, out var cle))
                continue;
            var jour = dateP.Date;
            if (jour < debut || jour > fin || !feries.Contains(jour))
                continue;

            var cleJour = (cle, jour);
            if (!planningRetenu.TryGetValue(cleJour, out var existant) || PrimeSur(plan, existant))
                planningRetenu[cleJour] = plan;
        }

        var joursFeriesAppliques = corrections
            .Where(h => h.DATE_POINTAGE_IN.HasValue && !string.IsNullOrWhiteSpace(h.MATRICULE_SAGE))
            .Where(h => h.FERIER == true || h.FERIER_AM == true || h.FERIER_PM == true || h.M_FERIES > 0)
            .Select(h => (Cle: MatriculeIdentity.Cle(h.MATRICULE_SAGE), Jour: h.DATE_POINTAGE_IN!.Value.Date))
            .ToHashSet();

        var resultats = new List<ClosingAnomaly>();
        foreach (var (cleJour, plan) in planningRetenu)
        {
            if (AbsenceReportRules.EstJourOff(plan))
                continue;
            var duree = AbsenceReportRules.CalculerDureeHeures(plan);
            if (duree is null || joursFeriesAppliques.Contains(cleJour))
                continue;

            resultats.Add(Anomalie(
                ClosingAnomalyType.FerieNonApplique, cleJour.Cle, cleJour.Jour,
                "Férié planifié sans correction férié", duree));
        }

        return Trier(resultats);
    }

    /// <summary>
    /// 3.9 — Score de clôture sur 100 (voir <see cref="FormuleScore"/>) :
    /// <c>100 − 100 × Σ poids ÷ (poids maximum × joursEmployeControles)</c>, borné à [0, 100],
    /// arrondi à l'entier (demi vers le haut). Renvoie <c>null</c> sans jour-employé contrôlé, ou
    /// quand tous les poids sont nuls (score non calculable).
    /// </summary>
    public static int? Score(
        IReadOnlyList<ClosingAnomaly> anomalies,
        int joursEmployeControles,
        ClosingRulesOptions? options = null)
    {
        options ??= ClosingRulesOptions.Default;
        if (joursEmployeControles <= 0 || options.PoidsMaximum <= 0)
            return null;

        var sommePoids = anomalies.Sum(a => (decimal)options.Poids(a.Severity));
        var score = 100m - 100m * sommePoids / (options.PoidsMaximum * (decimal)joursEmployeControles);
        score = Math.Clamp(score, 0m, 100m);
        return (int)Math.Round(score, MidpointRounding.AwayFromZero);
    }

    /// <summary>Ordre déterministe : date, matricule, type, puis détail.</summary>
    public static IReadOnlyList<ClosingAnomaly> Trier(IEnumerable<ClosingAnomaly> anomalies) =>
        anomalies
            .OrderBy(a => a.Date)
            .ThenBy(a => a.Matricule, StringComparer.Ordinal)
            .ThenBy(a => a.Type)
            .ThenBy(a => a.Detail, StringComparer.Ordinal)
            .ToList();

    private static ClosingAnomaly Anomalie(
        ClosingAnomalyType type, string cle, DateTime jour, string detail, decimal? heures = null)
        => new(type, GraviteParDefaut(type), cle, jour.Date, detail, heures);

    /// <summary>Pointages exploitables, avec le même filtre que le pré-traitement.</summary>
    private static IEnumerable<T_POINTAGE> PointagesValides(IEnumerable<T_POINTAGE> pointages) =>
        pointages.Where(p => p.DATE_POINTAGE.HasValue
                             && p.HEURE_POINTAGE.HasValue
                             && !string.IsNullOrWhiteSpace(p.MATRICULE_SAGE));

    private static HashSet<(string Cle, DateTime Jour)> JoursPointes(IEnumerable<T_POINTAGE> pointages) =>
        PointagesValides(pointages)
            .Select(p => (MatriculeIdentity.Cle(p.MATRICULE_SAGE), p.DATE_POINTAGE!.Value.Date))
            .ToHashSet();

    private static bool AHeuresCorrigees(T_HEURECORRIGER h) =>
        h.HENTREE.HasValue || h.HSORTIE.HasValue || h.HPS.HasValue || h.HPE.HasValue;

    /// <summary>Toutes les cartes d'une même clé matricule (règle C11 de l'état des absences).</summary>
    private static Dictionary<int, string> CleParIdSal(IEnumerable<T_CARDPAIE> cartes)
    {
        var resultat = new Dictionary<int, string>();
        foreach (var carte in cartes)
        {
            var cle = MatriculeIdentity.Cle(carte.SAGE_MATRICULE);
            if (cle.Length > 0)
                resultat[carte.ID] = cle;
        }
        return resultat;
    }

    /// <summary>Une carte par clé matricule : la plus petite ID, dans l'ordre des clés.</summary>
    private static IEnumerable<(string Cle, T_CARDPAIE Carte)> CartesParCle(IEnumerable<T_CARDPAIE> cartes) =>
        cartes
            .Select(c => (Cle: MatriculeIdentity.Cle(c.SAGE_MATRICULE), Carte: c))
            .Where(x => x.Cle.Length > 0)
            .GroupBy(x => x.Cle, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (g.Key, g.OrderBy(x => x.Carte.ID).First().Carte));

    /// <summary>Même départage que <see cref="AbsenceReportRules"/> : travail avant OFF, puis plus petit ID.</summary>
    private static bool PrimeSur(T_PLANNING_SAL candidat, T_PLANNING_SAL existant)
    {
        var candidatOff = AbsenceReportRules.EstJourOff(candidat);
        var existantOff = AbsenceReportRules.EstJourOff(existant);
        if (candidatOff != existantOff)
            return !candidatOff;
        return candidat.ID < existant.ID;
    }

    private static string FormatHeure(TimeSpan heure) => heure.ToString(@"hh\:mm\:ss");
}
