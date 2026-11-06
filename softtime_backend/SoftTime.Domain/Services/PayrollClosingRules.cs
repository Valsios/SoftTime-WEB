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

    /// <summary>
    /// Gravité par défaut de chaque type d'anomalie. Les doublons (exacts et quasi) dépendent aussi du
    /// mode de pointage : voir <see cref="ClosingRulesOptions.Gravite(ClosingAnomalyType, bool)"/>.
    /// </summary>
    public static ClosingSeverity GraviteParDefaut(ClosingAnomalyType type) => type switch
    {
        ClosingAnomalyType.PointageIncomplet => ClosingSeverity.Critique,
        ClosingAnomalyType.PointageManquant => ClosingSeverity.Critique,
        ClosingAnomalyType.PlanningSansPointage => ClosingSeverity.Importante,
        ClosingAnomalyType.PointageSansPlanning => ClosingSeverity.Importante,
        ClosingAnomalyType.FerieNonApplique => ClosingSeverity.Information,
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
        var debut = from.Date;
        var fin = to.Date;
        if (debut > fin)
            return Array.Empty<ClosingAnomaly>();

        var lundisValides = semainesValidees
            .Where(_ => options.IgnorerSemainesValidees)
            .Where(v => v.ValidationHs == true && v.DateDeb.HasValue && !string.IsNullOrWhiteSpace(v.Matricules))
            .GroupBy(v => MatriculeIdentity.Cle(v.Matricules))
            .ToDictionary(g => g.Key, g => g.Select(v => v.DateDeb!.Value.Date).ToHashSet());

        var joursManuels = corrections
            .Where(h => h.DatePointageIn.HasValue && !string.IsNullOrWhiteSpace(h.MatriculeSage))
            .Where(h => !string.Equals(h.Origine, OrigineAuto, StringComparison.OrdinalIgnoreCase))
            .Select(h => (Cle: MatriculeIdentity.Cle(h.MatriculeSage), Jour: h.DatePointageIn!.Value.Date))
            .ToHashSet();

        bool EstIgnore(string cle, DateTime jour)
        {
            if (joursManuels.Contains((cle, jour)))
                return true;
            var lundi = jour.AddDays(-(((int)jour.DayOfWeek + 6) % 7));
            return lundisValides.TryGetValue(cle, out var lundis) && lundis.Contains(lundi);
        }

        // Même planning que le pré-traitement : par carte et par jour, la ligne de plus petit ID.
        var planningParCarte = plannings
            .Where(p => p.DateP.HasValue)
            .GroupBy(p => p.IdSal)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(p => p.DateP!.Value.Date)
                    .ToDictionary(gg => gg.Key, gg => gg.OrderBy(p => p.Id).First()));

        var resultats = new List<ClosingAnomaly>();
        foreach (var (cle, carte) in CartesParCle(cartes))
        {
            var punchs = pointages.Moments(cle);
            if (punchs.Count == 0)
                continue;

            var planningCarte = planningParCarte.GetValueOrDefault(carte.Id)
                                ?? new Dictionary<DateTime, PlanningSalarie>();
            var fenetres = multiPointage
                ? planningCarte
                    .Where(kv => kv.Value.OffP != true)
                    .ToDictionary(
                        kv => kv.Key,
                        kv => new PunchPlanningWindow(kv.Key, kv.Value.Ha, kv.Value.Hd, kv.Value.OffP == true))
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
        IReadOnlyList<CardPaie> cartes,
        IReadOnlyList<PlanningSalarie> plannings,
        IReadOnlyList<Pointage> pointages,
        IReadOnlyList<HeureCorrigee> corrections,
        IReadOnlyDictionary<string, List<LeaveDay>> congesParCle,
        IReadOnlySet<DateTime> feries,
        DateTime from,
        DateTime to,
        DateTime dateDuJour)
        => PlanningsSansPointage(
            AbsenceReportRules.Calculer(plannings, cartes, corrections, congesParCle, feries, from, to, dateDuJour),
            ClosingPunchIndex.Build(pointages),
            corrections);

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

        var resultats = new List<ClosingAnomaly>();
        foreach (var ligne in lignesAbsence)
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
            if (pointages.APointage(cle, jour))
                continue;

            var corrsDuJour = correctionsParJour.GetValueOrDefault((cle, jour)) ?? new List<HeureCorrigee>();
            if (corrsDuJour.Any(AHeuresCorrigees))
                continue;

            // Congé d'une demi-journée : la moitié du congé n'est pas connue (matin ou après-midi),
            // une absence arbitrée le même jour peut donc couvrir l'autre moitié. Prudence : rien.
            if (demiConge && corrsDuJour.Any(h => h.AbsenceAm == true || h.AbsencePm == true))
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
        IReadOnlyList<CardPaie> cartes,
        IReadOnlyList<PlanningSalarie> plannings,
        IReadOnlyList<Pointage> pointages,
        DateTime from,
        DateTime to,
        ClosingRulesOptions? options = null)
        => PointagesSansPlanning(cartes, plannings, ClosingPunchIndex.Build(pointages), from, to, options);

    /// <summary>3.4 sur un index de pointages déjà construit.</summary>
    public static IReadOnlyList<ClosingAnomaly> PointagesSansPlanning(
        IReadOnlyList<CardPaie> cartes,
        IReadOnlyList<PlanningSalarie> plannings,
        ClosingPunchIndex pointages,
        DateTime from,
        DateTime to,
        ClosingRulesOptions? options = null)
    {
        options ??= ClosingRulesOptions.Default;
        var debut = from.Date;
        var fin = to.Date;
        var cleParIdSal = CleParIdSal(cartes);
        var exclus = options.ExclureSansPlanningSurPeriode
            ? SalariesSansPlanningSurPeriode(cartes, plannings, pointages, from, to)
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
            .Select(x => Anomalie(
                ClosingAnomalyType.PointageSansPlanning, x.Cle, x.Jour,
                $"{x.Nombre} pointage(s) sans ligne de planning"))
            .ToList();

        return Trier(resultats);
    }

    /// <summary>
    /// Salariés (clés) ayant au moins un pointage sur la période mais AUCUNE ligne de planning, de toute
    /// nature (travail, OFF, Pause), sur cette même période : ils ne sont pas contrôlés par la règle
    /// « pointage sans planning » quand <see cref="ClosingRulesOptions.ExclureSansPlanningSurPeriode"/>
    /// est vrai. Un salarié partiellement planifié n'en fait pas partie.
    /// </summary>
    public static IReadOnlySet<string> SalariesSansPlanningSurPeriode(
        IReadOnlyList<CardPaie> cartes,
        IReadOnlyList<PlanningSalarie> plannings,
        ClosingPunchIndex pointages,
        DateTime from,
        DateTime to)
    {
        var debut = from.Date;
        var fin = to.Date;
        var cleParIdSal = CleParIdSal(cartes);

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

    /// <summary>3.5 — Plusieurs pointages du même matricule à la même date et à la même heure exacte.</summary>
    public static IReadOnlyList<ClosingAnomaly> DoublonsExacts(
        IReadOnlyList<Pointage> pointages,
        DateTime from,
        DateTime to,
        ClosingRulesOptions? options = null,
        bool multiPointage = false)
        => DoublonsExacts(ClosingPunchIndex.Build(pointages), from, to, options, multiPointage);

    /// <summary>3.5 sur un index de pointages déjà construit (pointages triés : les doublons sont contigus).</summary>
    public static IReadOnlyList<ClosingAnomaly> DoublonsExacts(
        ClosingPunchIndex pointages,
        DateTime from,
        DateTime to,
        ClosingRulesOptions? options = null,
        bool multiPointage = false)
    {
        options ??= ClosingRulesOptions.Default;
        var gravite = options.Gravite(ClosingAnomalyType.DoublonExact, multiPointage);
        var debut = from.Date;
        var fin = to.Date;

        var resultats = new List<ClosingAnomaly>();
        foreach (var cle in pointages.Cles)
        {
            var moments = pointages.Moments(cle);
            for (var i = 0; i < moments.Count;)
            {
                var j = i + 1;
                while (j < moments.Count && moments[j] == moments[i])
                    j++;

                var jour = moments[i].Date;
                if (j - i > 1 && jour >= debut && jour <= fin)
                    resultats.Add(Anomalie(
                        ClosingAnomalyType.DoublonExact, cle, jour,
                        $"{j - i} pointages identiques à {FormatHeure(moments[i].Heure)} ({EffetDoublon(multiPointage)})",
                        gravite: gravite));
                i = j;
            }
        }

        return Trier(resultats);
    }

    /// <summary>
    /// 3.6 — Deux pointages successifs du même matricule séparés de strictement moins de
    /// <see cref="ClosingRulesOptions.DuplicateToleranceSeconds"/>. Les doublons exacts sont
    /// réduits à un seul instant avant la comparaison : ils ne sont jamais comptés deux fois.
    /// L'anomalie est datée du premier pointage de la paire.
    /// </summary>
    public static IReadOnlyList<ClosingAnomaly> QuasiDoublons(
        IReadOnlyList<Pointage> pointages,
        DateTime from,
        DateTime to,
        ClosingRulesOptions? options = null,
        bool multiPointage = false)
        => QuasiDoublons(ClosingPunchIndex.Build(pointages), from, to, options, multiPointage);

    /// <summary>3.6 sur un index de pointages déjà construit.</summary>
    public static IReadOnlyList<ClosingAnomaly> QuasiDoublons(
        ClosingPunchIndex pointages,
        DateTime from,
        DateTime to,
        ClosingRulesOptions? options = null,
        bool multiPointage = false)
    {
        options ??= ClosingRulesOptions.Default;
        var gravite = options.Gravite(ClosingAnomalyType.QuasiDoublon, multiPointage);
        var tolerance = TimeSpan.FromSeconds(Math.Max(0, options.DuplicateToleranceSeconds));
        var debut = from.Date;
        var fin = to.Date;

        var resultats = new List<ClosingAnomaly>();
        foreach (var cle in pointages.Cles)
        {
            // Pointages déjà triés : instants distincts de la période, dans l'ordre.
            var instants = new List<DateTime>();
            foreach (var m in pointages.Moments(cle))
            {
                if (m.Date < debut || m.Date > fin)
                    continue;
                var instant = m.Date + m.Heure;
                if (instants.Count == 0 || instants[^1] != instant)
                    instants.Add(instant);
            }

            for (var i = 1; i < instants.Count; i++)
            {
                var ecart = instants[i] - instants[i - 1];
                if (ecart >= tolerance)
                    continue;

                resultats.Add(Anomalie(
                    ClosingAnomalyType.QuasiDoublon, cle, instants[i - 1].Date,
                    $"Écart de {(int)ecart.TotalSeconds} s entre {FormatHeure(instants[i - 1].TimeOfDay)} et {FormatHeure(instants[i].TimeOfDay)} ({EffetDoublon(multiPointage)})",
                    gravite: gravite));
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
        IReadOnlyList<CardPaie> cartes,
        IReadOnlyList<PlanningSalarie> plannings,
        IReadOnlyList<HeureCorrigee> corrections,
        IReadOnlySet<DateTime> feries,
        DateTime from,
        DateTime to,
        DateTime dateDuJour,
        ClosingPunchIndex? pointages = null)
    {
        var debut = from.Date;
        var hier = dateDuJour.Date.AddDays(-1);
        var fin = to.Date < hier ? to.Date : hier;
        if (debut > fin)
            return Array.Empty<ClosingAnomaly>();

        var cleParIdSal = CleParIdSal(cartes);

        var planningRetenu = new Dictionary<(string Cle, DateTime Jour), PlanningSalarie>();
        foreach (var plan in plannings)
        {
            if (plan.DateP is not DateTime dateP || !cleParIdSal.TryGetValue(plan.IdSal, out var cle))
                continue;
            var jour = dateP.Date;
            if (jour < debut || jour > fin || !feries.Contains(jour))
                continue;

            var cleJour = (cle, jour);
            if (!planningRetenu.TryGetValue(cleJour, out var existant) || PrimeSur(plan, existant))
                planningRetenu[cleJour] = plan;
        }

        var joursFeriesAppliques = corrections
            .Where(h => h.DatePointageIn.HasValue && !string.IsNullOrWhiteSpace(h.MatriculeSage))
            .Where(h => h.Ferier == true || h.FerierAm == true || h.FerierPm == true || h.MFeries > 0)
            .Select(h => (Cle: MatriculeIdentity.Cle(h.MatriculeSage), Jour: h.DatePointageIn!.Value.Date))
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
                "drapeau férié absent (sans effet sur le calcul de la paie)", duree,
                pointageExiste: pointages?.APointage(cleJour.Cle, cleJour.Jour)));
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

    /// <summary>
    /// Applique les gravités configurées (<see cref="ClosingRulesOptions.Gravite"/>) aux anomalies ;
    /// l'ordre est conservé (la gravité ne fait pas partie du tri). Sans gravité configurée, la liste
    /// est renvoyée telle quelle.
    /// </summary>
    public static IReadOnlyList<ClosingAnomaly> AppliquerGravites(IReadOnlyList<ClosingAnomaly> anomalies, ClosingRulesOptions options)
    {
        if (options.Gravites.Count == 0)
            return anomalies;
        // Seuls les types fixés explicitement sont remplacés : la gravité des autres (dont celle des
        // doublons, qui dépend du mode) a déjà été posée par la règle.
        return anomalies
            .Select(a => options.Gravites.TryGetValue(a.Type, out var gravite) && gravite != a.Severity ? a with { Severity = gravite } : a)
            .ToList();
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
        ClosingAnomalyType type, string cle, DateTime jour, string detail, decimal? heures = null,
        ClosingSeverity? gravite = null, bool? pointageExiste = null)
        => new(type, gravite ?? GraviteParDefaut(type), cle, jour.Date, detail, heures, pointageExiste);

    private static string EffetDoublon(bool multiPointage) =>
        multiPointage ? "double passage sans effet sur la paie (multi-pointage)" : "peut fausser les heures";

    private static bool AHeuresCorrigees(HeureCorrigee h) =>
        h.HEntree.HasValue || h.HSortie.HasValue || h.Hps.HasValue || h.Hpe.HasValue;

    /// <summary>Toutes les cartes d'une même clé matricule (règle C11 de l'état des absences).</summary>
    private static Dictionary<int, string> CleParIdSal(IEnumerable<CardPaie> cartes)
    {
        var resultat = new Dictionary<int, string>();
        foreach (var carte in cartes)
        {
            var cle = MatriculeIdentity.Cle(carte.SageMatricule);
            if (cle.Length > 0)
                resultat[carte.Id] = cle;
        }
        return resultat;
    }

    /// <summary>Une carte par clé matricule : la plus petite ID, dans l'ordre des clés.</summary>
    private static IEnumerable<(string Cle, CardPaie Carte)> CartesParCle(IEnumerable<CardPaie> cartes) =>
        cartes
            .Select(c => (Cle: MatriculeIdentity.Cle(c.SageMatricule), Carte: c))
            .Where(x => x.Cle.Length > 0)
            .GroupBy(x => x.Cle, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (g.Key, g.OrderBy(x => x.Carte.Id).First().Carte));

    /// <summary>Même départage que <see cref="AbsenceReportRules"/> : travail avant OFF, puis plus petit ID.</summary>
    private static bool PrimeSur(PlanningSalarie candidat, PlanningSalarie existant)
    {
        var candidatOff = AbsenceReportRules.EstJourOff(candidat);
        var existantOff = AbsenceReportRules.EstJourOff(existant);
        if (candidatOff != existantOff)
            return !candidatOff;
        return candidat.Id < existant.Id;
    }

    private static string FormatHeure(TimeSpan heure) => heure.ToString(@"hh\:mm\:ss");
}
