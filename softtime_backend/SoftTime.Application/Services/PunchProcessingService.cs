using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

/// <summary>
/// Pré-traitement des pointages : transforme les pointages bruts (<c>T_POINTAGE</c>) en heures
/// corrigées (<c>T_HEURECORRIGER</c>) et en anomalies à arbitrer (<c>T_HEUREANOMALIE</c>).
/// Portage de l'ancien <c>ImportationPointageService.Importation</c> de l'application desktop.
///
/// Règles d'appariement : <see cref="SimpleDayRules"/> (mode simple) et
/// <see cref="PunchPairingRules"/> (mode multi-pointage).
/// Invariants :
/// <list type="bullet">
///   <item>seules les lignes <c>ORIGINE = 'AUTO'</c> de la période sont purgées puis régénérées ;</item>
///   <item>les corrections manuelles (ORIGINE 'MANUEL' ou NULL) et les semaines validées
///     (<c>VALIDATION_HS</c>) ne sont jamais touchées ;</item>
///   <item>les anomalies sont additives : jamais deux anomalies pour un même (matricule, jour), et les
///     anomalies d'un jour redevenu cohérent sont supprimées ;</item>
///   <item>les jours non travaillés (samedi/dimanche, <c>OffP</c>) et les fériés non travaillés ne
///     produisent ni heure ni anomalie, sauf option <c>ANOMALIE_HORS_PLANNING</c>.</item>
/// </list>
/// Les paramètres sont lus dans <c>T_CLOCK</c> ; leurs valeurs nulles reproduisent le comportement
/// historique. Le calcul est séparé de l'écriture (<see cref="BuildPlanAsync"/> /
/// <see cref="ApplyAsync"/>) afin de permettre une simulation sans écriture.
/// </summary>
public class PunchProcessingService
{
    /// <summary>Nombre maximal de lignes détaillées renvoyées par une simulation.</summary>
    private const int MaxDetails = 500;

    private readonly IUnitOfWork _uow;
    private readonly TenantConnectionService _tenant;
    private readonly SageLeaveCalendar _leaves;

    public PunchProcessingService(IUnitOfWork uow, TenantConnectionService tenant, SageLeaveCalendar leaves)
    {
        _uow = uow;
        _tenant = tenant;
        _leaves = leaves;
    }

    /// <param name="request">Période et filtres.</param>
    /// <param name="dryRun">
    /// <c>true</c> : calcule et renvoie les actions prévues sans rien écrire (simulation).
    /// </param>
    public async Task<PreprocessPunchesResultDto> ProcessAsync(
        PeriodRequest request, bool dryRun = false, CancellationToken ct = default)
    {
        var plan = await BuildPlanAsync(request, ct);
        if (!dryRun)
            await ApplyAsync(plan, ct);
        return BuildResult(plan, dryRun);
    }

    // ------------------------------------------------------------------ Calcul (sans écriture)

    private async Task<PreprocessPlan> BuildPlanAsync(PeriodRequest request, CancellationToken ct)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        await PunchProcessingConfiguration.EnsureStorageAsync(_uow, ct);

        var clock = (await _uow.Repository<T_CLOCK>().ListAsync(_ => true, ct)).FirstOrDefault()
            ?? throw new InvalidOperationException("Paramètre pointeuse (T_CLOCK) manquant.");
        var multiPointage = clock.MULTIPOINT == true;
        var strict = clock.SIMPLE_STRICT == true && !multiPointage;
        var filtrePointeuse = clock.FILTRE_POINTEUSE == true;
        var anomalieHorsPlanning = clock.ANOMALIE_HORS_PLANNING == true;
        var simpleNuit = clock.SIMPLE_NUIT == true && !multiPointage;
        var options = strict
            ? new SimpleDayOptions(
                Strict: true,
                MinDuration: Minutes(clock.DUREE_MIN_JOUR),
                MaxDuration: Minutes(clock.DUREE_MAX_JOUR))
            : SimpleDayOptions.ParDefaut;

        var sageDb = _tenant.SageDb;
        var pointeuseDb = _tenant.PointeuseDb;
        var from = request.From.Date;
        var to = request.To.Date;
        var finExclusive = to.AddDays(1);
        // Une vacation de nuit commencée le dernier jour de la période sort le lendemain : on charge
        // les pointages correspondants pour fermer la paire.
        var limitePointages = simpleNuit ? to.AddDays(2) : finExclusive;

        var cards = FilterCards(await _uow.Repository<T_CARDPAIE>().ListAsync(c => c.SAGE_BDD == sageDb, ct), request);
        var feries = (await _uow.Repository<T_FERIE>().ListAsync(f => f.BDD_SAGE == sageDb, ct))
            .Where(f => f.DATE.HasValue)
            .Select(f => f.DATE!.Value.Date)
            .ToHashSet();
        var planning = await _uow.Repository<T_PLANNING_SAL>().ListAsync(
            p => p.BDD_SAGE == sageDb && p.DateP >= from && p.DateP < finExclusive, ct);
        var semainesValidees = (await _uow.Repository<T_HS_VALIDER>().ListAsync(
                v => v.BDD_SAGE == sageDb && v.VALIDATION_HS == true, ct))
            .Where(v => v.DATE_DEB.HasValue)
            .ToList();
        var correctionsExistantes = await _uow.Repository<T_HEURECORRIGER>().ListAsync(
            h => h.BDD_SAGE == sageDb && h.DATE_POINTAGE_IN >= from && h.DATE_POINTAGE_IN < finExclusive, ct);
        var anomaliesExistantes = await _uow.Repository<T_HEUREANOMALIE>().ListAsync(
            a => a.BDD_SAGE == sageDb && a.DATE_POINTAGE_IN >= from && a.DATE_POINTAGE_IN < finExclusive, ct);
        var pointages = await _uow.Repository<T_POINTAGE>().ListAsync(
            p => p.NOM_BDD_SAGE == sageDb && p.DATE_POINTAGE >= from && p.DATE_POINTAGE < limitePointages, ct);
        var conges = await ChargerCongesAsync(ct);

        var plan = new PreprocessPlan();

        // Filtre optionnel : ne garder que la base pointeuse active (les lignes héritées sans base
        // renseignée restent prises en compte).
        if (filtrePointeuse && !string.IsNullOrWhiteSpace(pointeuseDb))
        {
            var retenus = pointages
                .Where(p => p.NOM_BDD_POINTEUSE is null
                            || string.Equals(p.NOM_BDD_POINTEUSE.Trim(), pointeuseDb.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
            plan.PointagesEcartes = pointages.Count - retenus.Count;
            pointages = retenus;
        }

        // Purge des lignes issues du pré-traitement ; tout le reste est protégé.
        var aPurger = correctionsExistantes
            .Where(h => string.Equals(h.ORIGINE, PunchProcessingConfiguration.OrigineAuto, StringComparison.OrdinalIgnoreCase))
            .ToList();
        plan.CorrectionsAPurger.AddRange(aPurger);
        var protections = correctionsExistantes.Except(aPurger).ToList();

        var anomaliesASupprimer = new HashSet<decimal>();
        var traitees = new HashSet<(string Matricule, DateTime Jour)>();

        void RetirerAnomalies(string matricule, DateTime jour)
        {
            foreach (var anomalie in anomaliesExistantes.Where(a =>
                         !anomaliesASupprimer.Contains(a.ID)
                         && MemeIdentifiant(a.MATRICULE_SAGE, matricule)
                         && a.DATE_POINTAGE_IN?.Date == jour))
                anomaliesASupprimer.Add(anomalie.ID);
        }

        bool DejaUneAnomalie(string matricule, DateTime jour) =>
            traitees.Contains((matricule, jour))
            || anomaliesExistantes.Any(a => !anomaliesASupprimer.Contains(a.ID)
                                            && MemeIdentifiant(a.MATRICULE_SAGE, matricule)
                                            && a.DATE_POINTAGE_IN?.Date == jour);

        void AjouterAnomalie(
            string matricule, DateTime jour, string motif,
            TimeSpan? h1, TimeSpan? h2, bool? absenceAm, bool? absencePm, bool estFerie)
        {
            if (DejaUneAnomalie(matricule, jour))
                return;
            plan.Anomalies.Add(new PreprocessAnomalyPlan(
                matricule, jour, motif, h1, h2, absenceAm, absencePm, estFerie));
            traitees.Add((matricule, jour));
        }

        foreach (var card in cards)
        {
            var matricule = (card.SAGE_MATRICULE ?? string.Empty).Trim();
            if (matricule.Length == 0)
                continue;

            var joursConge = conges.TryGetValue(MatriculeIdentity.Cle(matricule), out var jours)
                ? jours
                : new List<LeaveDay>();
            var pointagesCarte = pointages
                .Where(p => p.DATE_POINTAGE.HasValue && p.HEURE_POINTAGE.HasValue
                            && MemeIdentifiant(p.MATRICULE_SAGE, matricule))
                .ToList();
            var planningCarte = planning
                .Where(p => p.IDSAL == card.ID && p.DateP.HasValue)
                .ToList();
            // Un salarié sans aucun jour planifié n'a pas de « hors planning » identifiable :
            // on n'ajoute pas d'anomalie pour ne pas noyer l'écran.
            var carteAPlanning = card.ACTIF && planningCarte.Any(p => p.OffP != true);
            var joursNuit = simpleNuit
                ? planningCarte
                    .Where(p => p.OffP != true && JourneeRules.EstNuit(p.HA, p.HD))
                    .Select(p => p.DateP!.Value.Date)
                    .ToHashSet()
                : new HashSet<DateTime>();
            // Pointages déjà consommés par la vacation de nuit de la veille.
            var consommes = new HashSet<decimal>();
            var joursCouverts = new HashSet<DateTime>();

            for (var jour = from; jour <= to; jour = jour.AddDays(1))
            {
                if (joursCouverts.Contains(jour))
                    continue;

                var planDuJour = planningCarte.FirstOrDefault(p => p.DateP!.Value.Date == jour);
                var estNuit = joursNuit.Contains(jour);
                // Week-end et OffP sont ignorés (comportement historique) ; l'absence de ligne de
                // planning, elle, n'est jamais un motif d'ignorement : sinon un salarié sans
                // planning ne serait plus pointé du tout.
                var horsPlanning = !estNuit && JourneeRules.EstHorsPlanning(
                    jour,
                    aLignePlanning: planDuJour is not null,
                    ligneOff: planDuJour?.OffP == true,
                    carteAPlanning: carteAPlanning,
                    signalerLesTrous: anomalieHorsPlanning);

                if (horsPlanning && !(anomalieHorsPlanning && carteAPlanning))
                    continue;

                if (EstProtege(matricule, jour, protections, semainesValidees))
                {
                    plan.JoursIgnores++;
                    continue;
                }

                // Fenêtre de la vacation de nuit : de midi la veille au lendemain midi, bornée au
                // début du poste suivant pour ne pas lui voler ses pointages.
                DateTime? finFenetre = null;
                if (estNuit)
                {
                    finFenetre = jour.AddDays(1).AddHours(12);
                    var posteSuivant = planningCarte.FirstOrDefault(p =>
                        p.DateP!.Value.Date == jour.AddDays(1) && p.OffP != true && p.HA != null);
                    if (posteSuivant is not null
                        && posteSuivant.HA!.Value > jour.AddHours(12)
                        && posteSuivant.HA.Value < finFenetre)
                        finFenetre = posteSuivant.HA.Value;
                }

                var punchesJour = pointagesCarte
                    .Where(p => !consommes.Contains(p.IDPOINTAGE) && DansFenetre(p, jour, estNuit, finFenetre))
                    .ToList();
                var moments = punchesJour
                    .Select(p => new PunchMoment(p.DATE_POINTAGE!.Value.Date, p.HEURE_POINTAGE!.Value))
                    .ToList();
                var estFerie = feries.Contains(jour);

                if (horsPlanning)
                {
                    if (moments.Count > 0)
                        AjouterAnomalie(matricule, jour, PunchAnomalyMotifs.HorsPlanning,
                            moments[0].Heure, null, null, null, estFerie);
                    continue;
                }

                if (estNuit)
                {
                    foreach (var punch in punchesJour)
                        consommes.Add(punch.IDPOINTAGE);
                    var lendemain = jour.AddDays(1);
                    var planLendemain = planningCarte.FirstOrDefault(p => p.DateP!.Value.Date == lendemain);
                    if (planLendemain is null || planLendemain.OffP == true)
                        joursCouverts.Add(lendemain);
                }

                // Un jour de congé SAGE sans pointage n'est jamais une anomalie (mode strict).
                if (strict && moments.Count == 0 && LeaveRules.EstConge(joursConge, jour))
                {
                    RetirerAnomalies(matricule, jour);
                    continue;
                }

                var resultat = multiPointage
                    ? Normaliser(PunchPairingRules.PairMultipleDay(moments), moments)
                    : SimpleDayRules.Pair(moments, options);

                if (resultat.Pairs.Count > 0)
                {
                    // La pause déduite du schéma à 4 pointages ne concerne qu'une paire unique.
                    var hps = resultat.Pairs.Count == 1 ? resultat.Hps : null;
                    var hpe = resultat.Pairs.Count == 1 ? resultat.Hpe : null;
                    foreach (var paire in resultat.Pairs)
                        plan.Corrections.Add(new PreprocessCorrectionPlan(
                            matricule, paire.EntreeDate.Date, paire.SortieDate.Date,
                            paire.EntreeHeure, paire.SortieHeure, hps, hpe, estFerie));

                    // Jour redevenu cohérent : ses anomalies sont résolues.
                    RetirerAnomalies(matricule, jour);
                    continue;
                }

                if (moments.Count == 0)
                {
                    // Férié non travaillé : rien à signaler (comportement du desktop).
                    if (estFerie)
                        continue;
                    AjouterAnomalie(matricule, jour, PunchAnomalyMotifs.AucunPointage,
                        null, null, true, true, false);
                    continue;
                }

                var motif = resultat.Motif ?? PunchAnomalyMotifs.PointagesIncoherents;
                var premiereHeure = resultat.Unpaired.Count > 0 ? resultat.Unpaired[0].Heure : (TimeSpan?)null;
                var derniereHeure = strict && resultat.Unpaired.Count > 1
                    ? resultat.Unpaired[^1].Heure
                    : (multiPointage ? null : premiereHeure);
                AjouterAnomalie(matricule, jour, motif, premiereHeure, derniereHeure, null, null, estFerie);
            }
        }

        plan.AnomaliesASupprimer.AddRange(anomaliesExistantes.Where(a => anomaliesASupprimer.Contains(a.ID)));
        return plan;
    }

    // ------------------------------------------------------------------ Écriture

    private async Task ApplyAsync(PreprocessPlan plan, CancellationToken ct)
    {
        var corrRepo = _uow.Repository<T_HEURECORRIGER>();
        var anomRepo = _uow.Repository<T_HEUREANOMALIE>();

        if (plan.CorrectionsAPurger.Count > 0)
            corrRepo.RemoveRange(plan.CorrectionsAPurger);

        foreach (var correction in plan.Corrections)
        {
            await corrRepo.AddAsync(new T_HEURECORRIGER
            {
                BDD_SAGE = _tenant.SageDb,
                BDD_POINTEUSE = _tenant.PointeuseDb,
                MATRICULE_SAGE = correction.Matricule,
                DATE_POINTAGE_IN = correction.DateIn,
                DATE_POINTAGE_OUT = correction.DateOut,
                HENTREE = correction.HEntree,
                HPS = correction.Hps,
                HPE = correction.Hpe,
                HSORTIE = correction.HSortie,
                FERIER = correction.EstFerie ? true : null,
                FERIER_AM = correction.EstFerie ? true : null,
                FERIER_PM = correction.EstFerie ? true : null,
                // Requis par l'état « absences » (ReportService filtre sur VALIDER_CORRECTION = true).
                VALIDER_CORRECTION = true,
                ORIGINE = PunchProcessingConfiguration.OrigineAuto
            }, ct);
        }

        foreach (var anomalie in plan.Anomalies)
        {
            await anomRepo.AddAsync(new T_HEUREANOMALIE
            {
                BDD_SAGE = _tenant.SageDb,
                BDD_POINTEUSE = _tenant.PointeuseDb,
                MATRICULE_SAGE = anomalie.Matricule,
                DATE_POINTAGE_IN = anomalie.Jour,
                DATE_POINTAGE_OUT = anomalie.Jour,
                H1 = anomalie.H1,
                H2 = anomalie.H2,
                ABS_AM = anomalie.AbsenceAm,
                ABS_PM = anomalie.AbsencePm,
                FERIES = anomalie.EstFerie ? true : null,
                FERIES_AM = anomalie.EstFerie ? true : null,
                FERIES_PM = anomalie.EstFerie ? true : null,
                MOTIF = anomalie.Motif
            }, ct);
        }

        if (plan.AnomaliesASupprimer.Count > 0)
            anomRepo.RemoveRange(plan.AnomaliesASupprimer);

        await _uow.SaveChangesAsync(ct);
    }

    private static PreprocessPunchesResultDto BuildResult(PreprocessPlan plan, bool dryRun)
    {
        var actions = plan.Corrections
            .Select(c => new PreprocessActionDto("CORRECTION", c.Matricule, c.DateIn, null, c.HEntree, c.HSortie))
            .Concat(plan.Anomalies.Select(a =>
                new PreprocessActionDto("ANOMALIE", a.Matricule, a.Jour, a.Motif, a.H1, a.H2)))
            .ToList();

        var message = $"{plan.Corrections.Count} correction(s) créée(s), {plan.Anomalies.Count} anomalie(s) à arbitrer"
            + (plan.AnomaliesASupprimer.Count > 0 ? $", {plan.AnomaliesASupprimer.Count} anomalie(s) résolue(s)" : string.Empty)
            + (plan.JoursIgnores > 0 ? $", {plan.JoursIgnores} jour(s) protégé(s) ignoré(s)" : string.Empty)
            + (plan.PointagesEcartes > 0 ? $", {plan.PointagesEcartes} pointage(s) hors base pointeuse" : string.Empty)
            + (dryRun ? " — simulation, aucune écriture effectuée." : ".");

        return new PreprocessPunchesResultDto(
            plan.Corrections.Count,
            plan.Anomalies.Count,
            plan.JoursIgnores,
            message,
            DryRun: dryRun,
            PointagesEcartes: plan.PointagesEcartes,
            AnomaliesSupprimees: plan.AnomaliesASupprimer.Count,
            Details: dryRun ? actions.Take(MaxDetails).ToList() : null,
            DetailsTronques: actions.Count > MaxDetails);
    }

    // ------------------------------------------------------------------ Helpers

    /// <summary>Charge les congés SAGE ; un échec de la source ne doit pas bloquer le pré-traitement.</summary>
    private async Task<Dictionary<string, List<LeaveDay>>> ChargerCongesAsync(CancellationToken ct)
    {
        try
        {
            return await _leaves.LoadAsync(ct);
        }
        catch (Exception)
        {
            // Enrichissement best-effort : sans congés, on retombe sur le comportement historique.
            return new Dictionary<string, List<LeaveDay>>(StringComparer.Ordinal);
        }
    }

    private static TimeSpan? Minutes(int? minutes) =>
        minutes is > 0 ? TimeSpan.FromMinutes(minutes.Value) : null;

    /// <summary>Enveloppe un résultat multi-pointage dans le modèle commun (pour le motif).</summary>
    private static SimpleDayResult Normaliser(PunchPairingResult resultat, IReadOnlyList<PunchMoment> moments) => new()
    {
        Pairs = resultat.Pairs,
        Unpaired = resultat.Unpaired,
        Motif = resultat.Pairs.Count > 0 ? null
            : moments.Count == 0 ? PunchAnomalyMotifs.AucunPointage
            : moments.Count == 1 ? PunchAnomalyMotifs.PointageUnique
            : PunchAnomalyMotifs.PointagesIncoherents
    };

    /// <summary>
    /// Pointage retenu pour le jour : date calendaire, ou fenêtre « vacation » (12 h autour du
    /// début de poste) pour un poste de nuit.
    /// </summary>
    private static bool DansFenetre(T_POINTAGE punch, DateTime jour, bool estNuit, DateTime? finFenetre)
    {
        var date = punch.DATE_POINTAGE!.Value.Date;
        if (!estNuit)
            return date == jour;
        var moment = date.Add(punch.HEURE_POINTAGE!.Value);
        var fin = finFenetre ?? jour.AddDays(1).AddHours(12);
        return moment >= jour.AddHours(12) && moment < fin;
    }

    /// <summary>Une journée est protégée si une correction manuelle existe, ou si sa semaine est validée.</summary>
    private static bool EstProtege(
        string matricule,
        DateTime jour,
        IReadOnlyList<T_HEURECORRIGER> protections,
        IReadOnlyList<T_HS_VALIDER> semainesValidees)
    {
        if (protections.Any(h => MemeIdentifiant(h.MATRICULE_SAGE, matricule) && h.DATE_POINTAGE_IN?.Date == jour))
            return true;
        var lundi = jour.AddDays(-(((int)jour.DayOfWeek + 6) % 7));
        return semainesValidees.Any(v =>
            MemeIdentifiant(v.MATRICULES, matricule) && v.DATE_DEB!.Value.Date == lundi);
    }

    /// <summary>Mêmes filtres que <c>OvertimeService.TargetCards</c> : le périmètre doit rester identique.</summary>
    private static List<T_CARDPAIE> FilterCards(List<T_CARDPAIE> cards, PeriodRequest request) =>
        cards
            .Where(c => string.IsNullOrEmpty(request.Branche) || c.BRANCHE == request.Branche)
            .Where(c => string.IsNullOrEmpty(request.MatriculeFrom)
                        || string.Compare(c.SAGE_MATRICULE, request.MatriculeFrom, StringComparison.Ordinal) >= 0)
            .Where(c => string.IsNullOrEmpty(request.MatriculeTo)
                        || string.Compare(c.SAGE_MATRICULE, request.MatriculeTo, StringComparison.Ordinal) <= 0)
            .ToList();

    /// <summary>Comparaison de matricule insensible au remplissage par des zéros (« 00369 » == « 369 »).</summary>
    private static bool MemeIdentifiant(string? a, string? b)
    {
        a = a?.Trim();
        b = b?.Trim();
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;
        if (long.TryParse(a, out var na) && long.TryParse(b, out var nb))
            return na == nb;
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
