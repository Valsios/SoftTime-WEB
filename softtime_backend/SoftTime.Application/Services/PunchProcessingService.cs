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
/// Règles d'appariement : <see cref="PunchPairingRules"/>.
/// Invariants :
/// <list type="bullet">
///   <item>seules les lignes <c>ORIGINE = 'AUTO'</c> de la période sont purgées puis régénérées ;</item>
///   <item>les corrections manuelles (ORIGINE 'MANUEL' ou NULL) et les semaines validées
///     (<c>VALIDATION_HS</c>) ne sont jamais touchées ;</item>
///   <item>les anomalies sont additives : jamais deux anomalies pour un même (matricule, jour), et les
///     anomalies d'un jour redevenu cohérent sont supprimées ;</item>
///   <item>les jours non travaillés (samedi/dimanche, <c>OffP</c>) et les fériés non travaillés ne
///     produisent ni heure ni anomalie.</item>
/// </list>
/// </summary>
public class PunchProcessingService
{
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

        var clock = (await _uow.Repository<T_CLOCK>().ListAsync(_ => true, ct)).FirstOrDefault()
            ?? throw new InvalidOperationException("Paramètre pointeuse (T_CLOCK) manquant.");
        var multiPointage = clock.MULTIPOINT == true;

        var sageDb = _tenant.SageDb;
        var pointeuseDb = _tenant.PointeuseDb;
        var from = request.From.Date;
        var to = request.To.Date;
        var finExclusive = to.AddDays(1);

        var cards = FilterCards(await _uow.Repository<T_CARDPAIE>().ListAsync(c => c.SAGE_BDD == sageDb, ct), request);
        var feries = (await _uow.Repository<T_FERIE>().ListAsync(f => f.BDD_SAGE == sageDb, ct))
            .Where(f => f.DATE.HasValue)
            .Select(f => f.DATE!.Value.Date)
            .ToHashSet();
        var planning = await _uow.Repository<T_PLANNING_SAL>().ListAsync(
            p => p.BDD_SAGE == sageDb && p.DateP >= from && p.DateP <= finExclusive, ct);
        var semainesValidees = (await _uow.Repository<T_HS_VALIDER>().ListAsync(
                v => v.BDD_SAGE == sageDb && v.VALIDATION_HS == true, ct))
            .Where(v => v.DATE_DEB.HasValue)
            .ToList();
        var correctionsExistantes = await _uow.Repository<T_HEURECORRIGER>().ListAsync(
            h => h.BDD_SAGE == sageDb && h.DATE_POINTAGE_IN >= from && h.DATE_POINTAGE_IN <= finExclusive, ct);
        var anomaliesExistantes = await _uow.Repository<T_HEUREANOMALIE>().ListAsync(
            a => a.BDD_SAGE == sageDb && a.DATE_POINTAGE_IN >= from && a.DATE_POINTAGE_IN <= finExclusive, ct);
        var pointages = await _uow.Repository<T_POINTAGE>().ListAsync(
            p => p.NOM_BDD_SAGE == sageDb && p.DATE_POINTAGE >= from && p.DATE_POINTAGE <= finExclusive, ct);

        var corrRepo = _uow.Repository<T_HEURECORRIGER>();
        var anomRepo = _uow.Repository<T_HEUREANOMALIE>();

        // Purge des lignes issues du pré-traitement ; tout le reste est protégé.
        var aPurger = correctionsExistantes
            .Where(h => string.Equals(h.ORIGINE, PunchProcessingConfiguration.OrigineAuto, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (aPurger.Count > 0)
            corrRepo.RemoveRange(aPurger);
        var protections = correctionsExistantes.Except(aPurger).ToList();

        var anomaliesASupprimer = new HashSet<decimal>();
        var anomaliesTraitees = new HashSet<(string Matricule, DateTime Jour)>();
        var correctionsCreees = 0;
        var anomaliesCreees = 0;
        var joursIgnores = 0;

        bool DejaUneAnomalie(string matricule, DateTime jour) =>
            anomaliesTraitees.Contains((matricule, jour))
            || anomaliesExistantes.Any(a => !anomaliesASupprimer.Contains(a.ID)
                                            && MemeIdentifiant(a.MATRICULE_SAGE, matricule)
                                            && a.DATE_POINTAGE_IN?.Date == jour);

        async Task AjouterAnomalieAsync(string matricule, DateTime jour, TimeSpan? h1, TimeSpan? h2, bool? absenceAm, bool? absencePm, bool estFerie)
        {
            if (DejaUneAnomalie(matricule, jour))
                return;
            await anomRepo.AddAsync(new T_HEUREANOMALIE
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
            }, ct);
            anomaliesTraitees.Add((matricule, jour));
            anomaliesCreees++;
        }

        foreach (var card in cards)
        {
            var matricule = (card.SAGE_MATRICULE ?? string.Empty).Trim();
            if (matricule.Length == 0)
                continue;

            var pointagesCarte = pointages
                .Where(p => p.DATE_POINTAGE.HasValue && p.HEURE_POINTAGE.HasValue
                            && MemeIdentifiant(p.MATRICULE_SAGE, matricule))
                .ToList();

            for (var jour = from; jour <= to; jour = jour.AddDays(1))
            {
                if (jour.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    continue;
                var plan = planning.FirstOrDefault(p =>
                    p.IDSAL == card.ID && p.DateP.HasValue && p.DateP.Value.Date == jour);
                if (plan?.OffP == true)
                    continue;

                if (EstProtege(matricule, jour, protections, semainesValidees))
                {
                    joursIgnores++;
                    continue;
                }

                var moments = pointagesCarte
                    .Where(p => p.DATE_POINTAGE!.Value.Date == jour)
                    .Select(p => new PunchMoment(jour, p.HEURE_POINTAGE!.Value))
                    .ToList();
                var estFerie = feries.Contains(jour);
                var resultat = multiPointage
                    ? PunchPairingRules.PairMultipleDay(moments)
                    : PunchPairingRules.PairSimpleDay(moments);

                if (resultat.Pairs.Count > 0)
                {
                    foreach (var paire in resultat.Pairs)
                    {
                        await corrRepo.AddAsync(new T_HEURECORRIGER
                        {
                            BDD_SAGE = sageDb,
                            BDD_POINTEUSE = pointeuseDb,
                            MATRICULE_SAGE = matricule,
                            DATE_POINTAGE_IN = paire.EntreeDate.Date,
                            DATE_POINTAGE_OUT = paire.SortieDate.Date,
                            HENTREE = paire.EntreeHeure,
                            HSORTIE = paire.SortieHeure,
                            FERIER = estFerie ? true : null,
                            FERIER_AM = estFerie ? true : null,
                            FERIER_PM = estFerie ? true : null,
                            // Requis par l'état « absences » (ReportService filtre sur VALIDER_CORRECTION = true).
                            VALIDER_CORRECTION = true,
                            ORIGINE = PunchProcessingConfiguration.OrigineAuto
                        }, ct);
                        correctionsCreees++;
                    }

                    // Jour redevenu cohérent : ses anomalies sont résolues.
                    foreach (var anomalie in anomaliesExistantes.Where(a =>
                                 !anomaliesASupprimer.Contains(a.ID)
                                 && MemeIdentifiant(a.MATRICULE_SAGE, matricule)
                                 && a.DATE_POINTAGE_IN?.Date == jour))
                        anomaliesASupprimer.Add(anomalie.ID);
                    continue;
                }

                // Aucune paire : anomalie à arbitrer.
                if (moments.Count == 0)
                {
                    // Férié non travaillé : rien à signaler (comportement du desktop).
                    if (estFerie)
                        continue;
                    await AjouterAnomalieAsync(matricule, jour, null, null, true, true, false);
                    continue;
                }

                var premiereHeure = resultat.Unpaired.Count > 0 ? resultat.Unpaired[0].Heure : (TimeSpan?)null;
                await AjouterAnomalieAsync(
                    matricule, jour,
                    h1: premiereHeure,
                    h2: multiPointage ? null : premiereHeure,
                    absenceAm: null, absencePm: null, estFerie);
            }
        }

        if (anomaliesASupprimer.Count > 0)
            anomRepo.RemoveRange(anomaliesExistantes.Where(a => anomaliesASupprimer.Contains(a.ID)));

        await _uow.SaveChangesAsync(ct);

        var message = $"{correctionsCreees} correction(s) créée(s), {anomaliesCreees} anomalie(s) à arbitrer"
            + (joursIgnores > 0 ? $", {joursIgnores} jour(s) protégé(s) ignoré(s)" : string.Empty) + ".";
        return new PreprocessPunchesResultDto(correctionsCreees, anomaliesCreees, joursIgnores, message);
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
