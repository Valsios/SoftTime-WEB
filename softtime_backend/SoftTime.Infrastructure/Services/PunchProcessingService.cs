using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

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
public class PunchProcessingService : IPunchProcessingService
{
    private static readonly IReadOnlyDictionary<DateTime, PunchPlanningWindow> SansPlanning =
        new Dictionary<DateTime, PunchPlanningWindow>();

    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;

    public PunchProcessingService(IUnitOfWork uow, ITenantConnectionService tenant)
    {
        _uow = uow;
        _tenant = tenant;
    }

    public async Task<PreprocessPunchesResultDto> ProcessAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        await PunchProcessingConfiguration.EnsureStorageAsync(_uow, ct);

        if (request.From == default || request.To == default)
            throw new DomainException("La période (from/to) est requise pour traiter les pointages.");
        if (request.To.Date < request.From.Date)
            throw new DomainException("La date de fin doit être postérieure ou égale à la date de début.");

        var clock = (await _uow.Repository<ParametrePointeuse>().ListAsync(_ => true, ct)).FirstOrDefault()
            ?? throw new DomainException("Paramètre pointeuse (T_CLOCK) manquant.");
        var multiPointage = clock.MultiPoint == true;

        var sageDb = _tenant.SageDb;
        var pointeuseDb = _tenant.PointeuseDb;
        var from = request.From.Date;
        var to = request.To.Date;
        var finExclusive = to.AddDays(1);

        var cards = FilterCards(await _uow.Repository<CardPaie>().ListAsync(c => c.SageBdd == sageDb, ct), request)
            .Where(c => MatchesPointeuse(c.PointeuseBdd, pointeuseDb))
            .Where(c => !string.IsNullOrWhiteSpace(c.SageMatricule))
            .GroupBy(c => MatriculeIdentity.Cle(c.SageMatricule))
            .Select(g => g.OrderBy(c => c.Id).First())
            .ToList();

        var feries = (await _uow.Repository<JourFerie>().ListAsync(f => f.BddSage == sageDb, ct))
            .Where(f => f.Date.HasValue)
            .Select(f => f.Date!.Value.Date)
            .ToHashSet();

        var planning = (await _uow.Repository<PlanningSalarie>().ListAsync(
                p => p.BddSage == sageDb && p.DateP >= from.AddDays(-1) && p.DateP <= to.AddDays(1), ct))
            .Where(p => MatchesPointeuse(p.BddPointeuse, pointeuseDb))
            .Where(p => p.DateP.HasValue)
            .ToList();

        var planningParCarte = planning
            .GroupBy(p => p.IdSal)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(p => p.DateP!.Value.Date)
                    .ToDictionary(gg => gg.Key, gg => gg.OrderBy(p => p.Id).First()));

        var semainesValidees = (await _uow.Repository<HsValidee>().ListAsync(
                v => v.BddSage == sageDb && v.ValidationHs == true, ct))
            .Where(v => v.DateDeb.HasValue && !string.IsNullOrWhiteSpace(v.Matricules))
            .ToList();

        var correctionsExistantes = await _uow.Repository<HeureCorrigee>().ListAsync(
            h => h.BddSage == sageDb && h.DatePointageIn >= from && h.DatePointageIn < finExclusive, ct);
        var anomaliesExistantes = await _uow.Repository<HeureAnomalie>().ListAsync(
            a => a.BddSage == sageDb && a.DatePointageIn >= from && a.DatePointageIn < finExclusive, ct);
        var pointages = (await _uow.Repository<Pointage>().ListAsync(
                p => p.NomBddSage == sageDb && p.DatePointage >= from && p.DatePointage < finExclusive, ct))
            .Where(p => MatchesPointeuse(p.NomBddPointeuse, pointeuseDb))
            .Where(p => p.DatePointage.HasValue && p.HeurePointage.HasValue)
            .Where(p => !string.IsNullOrWhiteSpace(p.MatriculeSage))
            .ToList();

        var corrRepo = _uow.Repository<HeureCorrigee>();
        var anomRepo = _uow.Repository<HeureAnomalie>();

        var semainesProtegees = semainesValidees
            .GroupBy(v => MatriculeIdentity.Cle(v.Matricules))
            .ToDictionary(g => g.Key, g => g.Select(v => v.DateDeb!.Value.Date).ToHashSet());
        bool SemaineValidee(string cle, DateTime jour)
        {
            var lundi = jour.AddDays(-(((int)jour.DayOfWeek + 6) % 7));
            return semainesProtegees.TryGetValue(cle, out var lundis) && lundis.Contains(lundi);
        }

        // Purge des lignes issues du pré-traitement, limitée à ce qui va être régénéré : salariés du
        // périmètre demandé (département, plage de matricules, pointeuse) hors semaines validées.
        // Les autres lignes AUTO de la période ne sont pas retraitées : les effacer les perdrait.
        var clesPerimetre = cards
            .Select(c => MatriculeIdentity.Cle(c.SageMatricule))
            .ToHashSet(StringComparer.Ordinal);
        var aPurger = correctionsExistantes
            .Where(h => string.Equals(h.Origine, PunchProcessingConfiguration.OrigineAuto, StringComparison.OrdinalIgnoreCase))
            .Where(h => h.DatePointageIn.HasValue && !string.IsNullOrWhiteSpace(h.MatriculeSage))
            .Where(h => clesPerimetre.Contains(MatriculeIdentity.Cle(h.MatriculeSage)))
            .Where(h => !SemaineValidee(MatriculeIdentity.Cle(h.MatriculeSage), h.DatePointageIn!.Value.Date))
            .ToList();
        if (aPurger.Count > 0)
            corrRepo.RemoveRange(aPurger);

        var joursProteges = correctionsExistantes
            .Except(aPurger)
            .Where(h => h.DatePointageIn.HasValue && !string.IsNullOrWhiteSpace(h.MatriculeSage))
            .GroupBy(h => MatriculeIdentity.Cle(h.MatriculeSage))
            .ToDictionary(g => g.Key, g => g.Select(h => h.DatePointageIn!.Value.Date).ToHashSet());

        var pointagesParCle = pointages
            .GroupBy(p => MatriculeIdentity.Cle(p.MatriculeSage))
            .ToDictionary(
                g => g.Key,
                g => g.Select(p => new PunchMoment(p.DatePointage!.Value.Date, p.HeurePointage!.Value)).ToList());

        var anomaliesParJour = anomaliesExistantes
            .Where(a => a.DatePointageIn.HasValue && !string.IsNullOrWhiteSpace(a.MatriculeSage))
            .GroupBy(a => (Cle: MatriculeIdentity.Cle(a.MatriculeSage), Jour: a.DatePointageIn!.Value.Date))
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Id).ToList());

        var anomaliesASupprimer = new HashSet<decimal>();
        var anomaliesCreeesJour = new HashSet<(string Cle, DateTime Jour)>();
        var nouvellesCorrections = new List<HeureCorrigee>();
        var nouvellesAnomalies = new List<HeureAnomalie>();
        var correctionsCreees = 0;
        var anomaliesCreees = 0;
        var anomaliesMisesAJour = 0;
        var joursIgnores = 0;

        bool EstProtege(string cle, DateTime jour)
        {
            if (joursProteges.TryGetValue(cle, out var jours) && jours.Contains(jour))
                return true;
            return SemaineValidee(cle, jour);
        }

        void SupprimerAnomaliesDuJour(string cle, DateTime jour)
        {
            if (!anomaliesParJour.TryGetValue((cle, jour), out var existantes))
                return;
            foreach (var anomalie in existantes)
                anomaliesASupprimer.Add(anomalie.Id);
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
                var aGarder = existantes.Where(a => !anomaliesASupprimer.Contains(a.Id)).ToList();
                if (aGarder.Count > 0)
                {
                    var cible = aGarder[0];
                    cible.BddSage = sageDb;
                    cible.BddPointeuse = pointeuseDb;
                    cible.MatriculeSage = matricule;
                    cible.DatePointageIn = jour;
                    cible.DatePointageOut = jour;
                    cible.H1 = h1;
                    cible.H2 = h2;
                    cible.AbsAm = absenceAm;
                    cible.AbsPm = absencePm;
                    cible.Feries = estFerie ? true : null;
                    cible.FeriesAm = estFerie ? true : null;
                    cible.FeriesPm = estFerie ? true : null;
                    anomRepo.Update(cible);
                    foreach (var doublon in aGarder.Skip(1))
                        anomaliesASupprimer.Add(doublon.Id);
                    anomaliesMisesAJour++;
                    return;
                }
            }

            nouvellesAnomalies.Add(new HeureAnomalie
            {
                BddSage = sageDb,
                BddPointeuse = pointeuseDb,
                MatriculeSage = matricule,
                DatePointageIn = jour,
                DatePointageOut = jour,
                H1 = h1,
                H2 = h2,
                AbsAm = absenceAm,
                AbsPm = absencePm,
                Feries = estFerie ? true : null,
                FeriesAm = estFerie ? true : null,
                FeriesPm = estFerie ? true : null
            });
            anomaliesCreees++;
        }

        void AjouterCorrection(string matricule, PunchPair paire)
        {
            var ferieEntree = feries.Contains(paire.EntreeDate.Date);
            var ferieSortie = feries.Contains(paire.SortieDate.Date);
            nouvellesCorrections.Add(new HeureCorrigee
            {
                BddSage = sageDb,
                BddPointeuse = pointeuseDb,
                MatriculeSage = matricule,
                DatePointageIn = paire.EntreeDate.Date,
                DatePointageOut = paire.SortieDate.Date,
                HEntree = paire.EntreeHeure,
                HSortie = paire.SortieHeure,
                Ferier = ferieEntree || ferieSortie ? true : null,
                FerierAm = ferieSortie ? true : null,
                FerierPm = ferieEntree ? true : null,
                ValiderCorrection = true,
                Origine = PunchProcessingConfiguration.OrigineAuto
            });
            correctionsCreees++;
        }

        bool EstTravaille(DateTime jour, PlanningSalarie? plan, bool estFerie)
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
            var matricule = (card.SageMatricule ?? string.Empty).Trim();
            if (matricule.Length == 0)
                continue;

            var cle = MatriculeIdentity.Cle(matricule);
            var punchs = pointagesParCle.GetValueOrDefault(cle) ?? new List<PunchMoment>();
            var planningCarte = planningParCarte.GetValueOrDefault(card.Id)
                                 ?? new Dictionary<DateTime, PlanningSalarie>();
            var fenetres = planningCarte
                .Where(kv => kv.Value.OffP != true)
                .ToDictionary(
                    kv => kv.Key,
                    kv => new PunchPlanningWindow(kv.Key, kv.Value.Ha, kv.Value.Hd, kv.Value.OffP == true));
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
                .Where(a => anomaliesASupprimer.Contains(a.Id))
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

    /// <summary>Filtre de périmètre identique à <c>OvertimeService.TargetCardsAsync</c>.</summary>
    private static List<CardPaie> FilterCards(List<CardPaie> cards, PeriodRequest request) =>
        cards
            .Where(c => string.IsNullOrEmpty(request.Departement)
                        || string.Equals(c.Departement, request.Departement, StringComparison.OrdinalIgnoreCase))
            .Where(c => string.IsNullOrEmpty(request.MatriculeFrom)
                        || string.Compare(c.SageMatricule, request.MatriculeFrom, StringComparison.Ordinal) >= 0)
            .Where(c => string.IsNullOrEmpty(request.MatriculeTo)
                        || string.Compare(c.SageMatricule, request.MatriculeTo, StringComparison.Ordinal) <= 0)
            .ToList();

    /// <summary>Filtre tenant pointeuse, en tolérant les lignes héritées sans base renseignée.</summary>
    private static bool MatchesPointeuse(string? valeur, string pointeuseDb) =>
        string.IsNullOrWhiteSpace(valeur)
        || string.Equals(valeur.Trim(), pointeuseDb.Trim(), StringComparison.OrdinalIgnoreCase);
}
