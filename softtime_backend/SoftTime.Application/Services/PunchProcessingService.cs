using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

/// <summary>
/// Pré-traitement des pointages : transforme les pointages bruts (<c>T_POINTAGE</c>) en heures
/// corrigées (<c>T_HEURECORRIGER</c>) et en anomalies à arbitrer (<c>T_HEUREANOMALIE</c>).
/// Portage de <c>Frm_Import.importa</c> de l'application desktop.
///
/// Règles :
/// <list type="bullet">
///   <item>mono-pointage : regroupement par jour calendaire, premier pointage en entrée, dernier en
///     sortie, sans appariement planning ;</item>
///   <item>multi-pointage : regroupement par journée de planning (postes de nuit compris), seuil de
///     15 minutes, dernier pointage forcé en sortie, puis parité pair → corrections / impair →
///     anomalie ;</item>
///   <item>fériés lus dans <c>T_FERIE</c> (synchronisé depuis SAGE <c>EtatJour = 1</c>).</item>
/// </list>
///
/// Invariants :
/// <list type="bullet">
///   <item>seules les lignes <c>ORIGINE = 'AUTO'</c> de la période sont purgées puis régénérées ;</item>
///   <item>les corrections manuelles (ORIGINE 'MANUEL' ou NULL) et les semaines validées
///     (<c>VALIDATION_HS</c>) ne sont jamais touchées ;</item>
///   <item>les anomalies sont additives mais une seule par (matricule, jour) : une anomalie
///     existante est mise à jour, pas dupliquée ;</item>
///   <item>les jours non travaillés sans pointage ne produisent ni heure ni anomalie.</item>
/// </list>
/// </summary>
public class PunchProcessingService
{
    private static readonly IReadOnlyDictionary<DateTime, PunchPlanningWindow> SansPlanning =
        new Dictionary<DateTime, PunchPlanningWindow>();

    private readonly IUnitOfWork _uow;
    private readonly TenantConnectionService _tenant;

    public PunchProcessingService(IUnitOfWork uow, TenantConnectionService tenant)
    {
        _uow = uow;
        _tenant = tenant;
    }

    public async Task<PreprocessPunchesResultDto> ProcessAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        await PunchProcessingConfiguration.EnsureStorageAsync(_uow, ct);

        if (request.From == default || request.To == default)
            throw new InvalidOperationException("La période (from/to) est requise pour traiter les pointages.");
        if (request.To.Date < request.From.Date)
            throw new InvalidOperationException("La date de fin doit être postérieure ou égale à la date de début.");

        var clock = (await _uow.Repository<T_CLOCK>().ListAsync(_ => true, ct)).FirstOrDefault()
            ?? throw new InvalidOperationException("Paramètre pointeuse (T_CLOCK) manquant.");
        var multiPointage = clock.MULTIPOINT == true;

        var sageDb = _tenant.SageDb;
        var pointeuseDb = _tenant.PointeuseDb;
        var from = request.From.Date;
        var to = request.To.Date;
        var finExclusive = to.AddDays(1);

        var cards = FilterCards(await _uow.Repository<T_CARDPAIE>().ListAsync(c => c.SAGE_BDD == sageDb, ct), request)
            .Where(c => MatchesPointeuse(c.POINTEUSE_BDD, pointeuseDb))
            .Where(c => !string.IsNullOrWhiteSpace(c.SAGE_MATRICULE))
            .GroupBy(c => MatriculeIdentity.Cle(c.SAGE_MATRICULE))
            .Select(g => g.OrderBy(c => c.ID).First())
            .ToList();

        var feries = (await _uow.Repository<T_FERIE>().ListAsync(f => f.BDD_SAGE == sageDb, ct))
            .Where(f => f.DATE.HasValue)
            .Select(f => f.DATE!.Value.Date)
            .ToHashSet();

        var planning = (await _uow.Repository<T_PLANNING_SAL>().ListAsync(
                p => p.BDD_SAGE == sageDb && p.DateP >= from.AddDays(-1) && p.DateP <= to.AddDays(1), ct))
            .Where(p => MatchesPointeuse(p.BDD_POINTEUSE, pointeuseDb))
            .Where(p => p.DateP.HasValue)
            .ToList();

        var planningParCarte = planning
            .GroupBy(p => p.IDSAL)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(p => p.DateP!.Value.Date)
                    .ToDictionary(gg => gg.Key, gg => gg.OrderBy(p => p.ID).First()));

        var semainesValidees = (await _uow.Repository<T_HS_VALIDER>().ListAsync(
                v => v.BDD_SAGE == sageDb && v.VALIDATION_HS == true, ct))
            .Where(v => v.DATE_DEB.HasValue && !string.IsNullOrWhiteSpace(v.MATRICULES))
            .ToList();

        var correctionsExistantes = await _uow.Repository<T_HEURECORRIGER>().ListAsync(
            h => h.BDD_SAGE == sageDb && h.DATE_POINTAGE_IN >= from && h.DATE_POINTAGE_IN < finExclusive, ct);
        var anomaliesExistantes = await _uow.Repository<T_HEUREANOMALIE>().ListAsync(
            a => a.BDD_SAGE == sageDb && a.DATE_POINTAGE_IN >= from && a.DATE_POINTAGE_IN < finExclusive, ct);
        var pointages = (await _uow.Repository<T_POINTAGE>().ListAsync(
                p => p.NOM_BDD_SAGE == sageDb && p.DATE_POINTAGE >= from && p.DATE_POINTAGE < finExclusive, ct))
            .Where(p => MatchesPointeuse(p.NOM_BDD_POINTEUSE, pointeuseDb))
            .Where(p => p.DATE_POINTAGE.HasValue && p.HEURE_POINTAGE.HasValue)
            .Where(p => !string.IsNullOrWhiteSpace(p.MATRICULE_SAGE))
            .ToList();

        var corrRepo = _uow.Repository<T_HEURECORRIGER>();
        var anomRepo = _uow.Repository<T_HEUREANOMALIE>();

        // Purge des lignes issues du pré-traitement ; tout le reste est protégé.
        var aPurger = correctionsExistantes
            .Where(h => string.Equals(h.ORIGINE, PunchProcessingConfiguration.OrigineAuto, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (aPurger.Count > 0)
            corrRepo.RemoveRange(aPurger);

        var joursProteges = correctionsExistantes
            .Except(aPurger)
            .Where(h => h.DATE_POINTAGE_IN.HasValue && !string.IsNullOrWhiteSpace(h.MATRICULE_SAGE))
            .GroupBy(h => MatriculeIdentity.Cle(h.MATRICULE_SAGE))
            .ToDictionary(g => g.Key, g => g.Select(h => h.DATE_POINTAGE_IN!.Value.Date).ToHashSet());
        var semainesProtegees = semainesValidees
            .GroupBy(v => MatriculeIdentity.Cle(v.MATRICULES))
            .ToDictionary(g => g.Key, g => g.Select(v => v.DATE_DEB!.Value.Date).ToHashSet());

        var pointagesParCle = pointages
            .GroupBy(p => MatriculeIdentity.Cle(p.MATRICULE_SAGE))
            .ToDictionary(
                g => g.Key,
                g => g.Select(p => new PunchMoment(p.DATE_POINTAGE!.Value.Date, p.HEURE_POINTAGE!.Value)).ToList());

        var anomaliesParJour = anomaliesExistantes
            .Where(a => a.DATE_POINTAGE_IN.HasValue && !string.IsNullOrWhiteSpace(a.MATRICULE_SAGE))
            .GroupBy(a => (Cle: MatriculeIdentity.Cle(a.MATRICULE_SAGE), Jour: a.DATE_POINTAGE_IN!.Value.Date))
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.ID).ToList());

        var anomaliesASupprimer = new HashSet<decimal>();
        var anomaliesCreeesJour = new HashSet<(string Cle, DateTime Jour)>();
        var nouvellesCorrections = new List<T_HEURECORRIGER>();
        var nouvellesAnomalies = new List<T_HEUREANOMALIE>();
        var correctionsCreees = 0;
        var anomaliesCreees = 0;
        var anomaliesMisesAJour = 0;
        var joursIgnores = 0;

        bool EstProtege(string cle, DateTime jour)
        {
            if (joursProteges.TryGetValue(cle, out var jours) && jours.Contains(jour))
                return true;
            var lundi = jour.AddDays(-(((int)jour.DayOfWeek + 6) % 7));
            return semainesProtegees.TryGetValue(cle, out var lundis) && lundis.Contains(lundi);
        }

        void SupprimerAnomaliesDuJour(string cle, DateTime jour)
        {
            if (!anomaliesParJour.TryGetValue((cle, jour), out var existantes))
                return;
            foreach (var anomalie in existantes)
                anomaliesASupprimer.Add(anomalie.ID);
        }

        void AjouterAnomalie(
            string matricule,
            string cle,
            DateTime jour,
            TimeSpan? h1,
            TimeSpan? h2,
            bool? absenceAm,
            bool? absencePm,
            bool estFerie)
        {
            if (!anomaliesCreeesJour.Add((cle, jour)))
                return;

            if (anomaliesParJour.TryGetValue((cle, jour), out var existantes))
            {
                var aGarder = existantes.Where(a => !anomaliesASupprimer.Contains(a.ID)).ToList();
                if (aGarder.Count > 0)
                {
                    var cible = aGarder[0];
                    cible.BDD_SAGE = sageDb;
                    cible.BDD_POINTEUSE = pointeuseDb;
                    cible.MATRICULE_SAGE = matricule;
                    cible.DATE_POINTAGE_IN = jour;
                    cible.DATE_POINTAGE_OUT = jour;
                    cible.H1 = h1;
                    cible.H2 = h2;
                    cible.ABS_AM = absenceAm;
                    cible.ABS_PM = absencePm;
                    cible.FERIES = estFerie ? true : null;
                    cible.FERIES_AM = estFerie ? true : null;
                    cible.FERIES_PM = estFerie ? true : null;
                    anomRepo.Update(cible);
                    foreach (var doublon in aGarder.Skip(1))
                        anomaliesASupprimer.Add(doublon.ID);
                    anomaliesMisesAJour++;
                    return;
                }
            }

            nouvellesAnomalies.Add(new T_HEUREANOMALIE
            {
                BDD_SAGE = sageDb,
                BDD_POINTEUSE = pointeuseDb,
                MATRICULE_SAGE = matricule,
                DATE_POINTAGE_IN = jour,
                DATE_POINTAGE_OUT = jour,
                H1 = h1,
                H2 = h2,
                ABS_AM = absenceAm,
                ABS_PM = absencePm,
                FERIES = estFerie ? true : null,
                FERIES_AM = estFerie ? true : null,
                FERIES_PM = estFerie ? true : null
            });
            anomaliesCreees++;
        }

        void AjouterCorrection(string matricule, PunchPair paire)
        {
            var ferieEntree = feries.Contains(paire.EntreeDate.Date);
            var ferieSortie = feries.Contains(paire.SortieDate.Date);
            nouvellesCorrections.Add(new T_HEURECORRIGER
            {
                BDD_SAGE = sageDb,
                BDD_POINTEUSE = pointeuseDb,
                MATRICULE_SAGE = matricule,
                DATE_POINTAGE_IN = paire.EntreeDate.Date,
                DATE_POINTAGE_OUT = paire.SortieDate.Date,
                HENTREE = paire.EntreeHeure,
                HSORTIE = paire.SortieHeure,
                FERIER = ferieEntree || ferieSortie ? true : null,
                FERIER_AM = ferieSortie ? true : null,
                FERIER_PM = ferieEntree ? true : null,
                VALIDER_CORRECTION = true,
                ORIGINE = PunchProcessingConfiguration.OrigineAuto
            });
            correctionsCreees++;
        }

        bool EstTravaille(DateTime jour, T_PLANNING_SAL? plan, bool estFerie)
        {
            if (estFerie)
                return false;
            if (plan is not null)
                return plan.OffP != true;
            // En mono, le desktop signale les jours ouvrés vides même sans planning. En multi,
            // le planning est la référence : sans planning, aucune absence n'est attendue.
            return !multiPointage
                   && jour.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
        }

        foreach (var card in cards)
        {
            var matricule = (card.SAGE_MATRICULE ?? string.Empty).Trim();
            if (matricule.Length == 0)
                continue;

            var cle = MatriculeIdentity.Cle(matricule);
            var punchs = pointagesParCle.GetValueOrDefault(cle) ?? new List<PunchMoment>();
            var planningCarte = planningParCarte.GetValueOrDefault(card.ID)
                                 ?? new Dictionary<DateTime, T_PLANNING_SAL>();
            var fenetres = planningCarte
                .Where(kv => kv.Value.OffP != true)
                .ToDictionary(
                    kv => kv.Key,
                    kv => new PunchPlanningWindow(kv.Key, kv.Value.HA, kv.Value.HD, kv.Value.OffP == true));
            var momentsParJour = multiPointage
                ? PunchDayGrouping.Build(punchs, fenetres, from, to)
                // Le mode mono ne consulte jamais le planning pour l'appariement : chaque journée
                // calendaire est traitée séparément (même si un poste de nuit existe au planning).
                : PunchDayGrouping.Build(punchs, SansPlanning, from, to);

            for (var jour = from; jour <= to; jour = jour.AddDays(1))
            {
                if (EstProtege(cle, jour))
                {
                    joursIgnores++;
                    continue;
                }

                var moments = momentsParJour.TryGetValue(jour, out var liste)
                    ? liste
                    : Array.Empty<PunchMoment>();
                var plan = planningCarte.GetValueOrDefault(jour);
                var estFerie = feries.Contains(jour);
                var estTravaille = EstTravaille(jour, plan, estFerie);

                if (moments.Count == 0)
                {
                    if (estTravaille)
                        AjouterAnomalie(matricule, cle, jour, null, null, true, true, false);
                    continue;
                }

                if (!multiPointage)
                {
                    var resultat = PunchPairingRules.PairSimpleDay(moments);
                    if (resultat.Unpaired.Count > 0)
                    {
                        var premier = resultat.Unpaired[0];
                        AjouterAnomalie(matricule, cle, jour, premier.Heure, premier.Heure, null, null, estFerie);
                        continue;
                    }

                    foreach (var paire in resultat.Pairs)
                        AjouterCorrection(matricule, paire);
                    SupprimerAnomaliesDuJour(cle, jour);
                    continue;
                }

                var resultatMulti = PunchPairingRules.PairMultipleDay(moments);
                if (resultatMulti.Unpaired.Count > 0)
                {
                    var premier = resultatMulti.Unpaired[0];
                    var dernier = resultatMulti.Unpaired[^1];
                    AjouterAnomalie(matricule, cle, jour, premier.Heure, dernier.Heure, null, null, estFerie);
                    continue;
                }

                if (resultatMulti.Pairs.Count == 0)
                {
                    // Cas défensif : moments non vides mais aucune paire ni orphelin.
                    AjouterAnomalie(matricule, cle, jour, moments[0].Heure, moments[^1].Heure, null, null, estFerie);
                    continue;
                }

                foreach (var paire in resultatMulti.Pairs)
                    AjouterCorrection(matricule, paire);
                SupprimerAnomaliesDuJour(cle, jour);
            }
        }

        if (anomaliesASupprimer.Count > 0)
        {
            var aSupprimer = anomaliesExistantes
                .Where(a => anomaliesASupprimer.Contains(a.ID))
                .ToList();
            if (aSupprimer.Count > 0)
                anomRepo.RemoveRange(aSupprimer);
        }

        if (nouvellesCorrections.Count > 0)
            await corrRepo.AddRangeAsync(nouvellesCorrections, ct);
        if (nouvellesAnomalies.Count > 0)
            await anomRepo.AddRangeAsync(nouvellesAnomalies, ct);

        await _uow.SaveChangesAsync(ct);

        var message = $"{correctionsCreees} correction(s) créée(s), {anomaliesCreees} anomalie(s) à arbitrer"
            + (anomaliesMisesAJour > 0 ? $", {anomaliesMisesAJour} anomalie(s) mise(s) à jour" : string.Empty)
            + (joursIgnores > 0 ? $", {joursIgnores} jour(s) protégé(s) ignoré(s)" : string.Empty)
            + ".";
        return new PreprocessPunchesResultDto(correctionsCreees, anomaliesCreees, joursIgnores, message);
    }

    /// <summary>Filtre de périmètre identique à <c>OvertimeService.TargetCards</c>.</summary>
    private static List<T_CARDPAIE> FilterCards(List<T_CARDPAIE> cards, PeriodRequest request) =>
        cards
            .Where(c => string.IsNullOrEmpty(request.Branche) || c.BRANCHE == request.Branche)
            .Where(c => string.IsNullOrEmpty(request.MatriculeFrom)
                        || string.Compare(c.SAGE_MATRICULE, request.MatriculeFrom, StringComparison.Ordinal) >= 0)
            .Where(c => string.IsNullOrEmpty(request.MatriculeTo)
                        || string.Compare(c.SAGE_MATRICULE, request.MatriculeTo, StringComparison.Ordinal) <= 0)
            .ToList();

    /// <summary>Filtre tenant pointeuse, en tolérant les lignes héritées sans base renseignée.</summary>
    private static bool MatchesPointeuse(string? valeur, string pointeuseDb) =>
        string.IsNullOrWhiteSpace(valeur)
        || string.Equals(valeur.Trim(), pointeuseDb.Trim(), StringComparison.OrdinalIgnoreCase);
}
