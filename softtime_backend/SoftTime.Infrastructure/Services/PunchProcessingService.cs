using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Infrastructure.Services.PunchProcessing;

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
        ValidatePeriod(request);

        var data = await LoadDataAsync(request, ct);
        var protection = new PunchProtection(data);
        if (protection.CorrectionsAPurger.Count > 0)
            _uow.Repository<HeureCorrigee>().RemoveRange(protection.CorrectionsAPurger);

        var run = new PunchProcessingRun(data, protection, _uow.Repository<HeureAnomalie>());
        foreach (var card in data.Cards)
            run.TraiterCarte(card);

        await PersistAsync(run, ct);
        return BuildResult(run);
    }

    private static void ValidatePeriod(PeriodRequest request)
    {
        if (request.From == default || request.To == default)
            throw new DomainException("La période (from/to) est requise pour traiter les pointages.");
        if (request.To.Date < request.From.Date)
            throw new DomainException("La date de fin doit être postérieure ou égale à la date de début.");
    }

    private async Task<PunchProcessingData> LoadDataAsync(PeriodRequest request, CancellationToken ct)
    {
        var clock = (await _uow.Repository<ParametrePointeuse>().ListAsync(_ => true, ct)).FirstOrDefault()
            ?? throw new DomainException("Paramètre pointeuse (T_CLOCK) manquant.");
        var sageDb = _tenant.SageDb;
        var from = request.From.Date;
        var to = request.To.Date;
        var finExclusive = to.AddDays(1);

        return new PunchProcessingData
        {
            SageDb = sageDb,
            PointeuseDb = _tenant.PointeuseDb,
            MultiPointage = clock.MultiPoint == true,
            From = from,
            To = to,
            Cards = await LoadCardsAsync(request, ct),
            Feries = await LoadFeriesAsync(ct),
            PlanningParCarte = await LoadPlanningParCarteAsync(from, to, ct),
            SemainesValidees = await LoadSemainesValideesAsync(ct),
            CorrectionsExistantes = await _uow.Repository<HeureCorrigee>().ListAsync(
                h => h.BddSage == sageDb && h.DatePointageIn >= from && h.DatePointageIn < finExclusive, ct),
            AnomaliesExistantes = await _uow.Repository<HeureAnomalie>().ListAsync(
                a => a.BddSage == sageDb && a.DatePointageIn >= from && a.DatePointageIn < finExclusive, ct),
            PointagesParCle = await LoadPointagesParCleAsync(from, finExclusive, ct)
        };
    }

    private async Task<List<CardPaie>> LoadCardsAsync(PeriodRequest request, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        var pointeuseDb = _tenant.PointeuseDb;
        return FilterCards(await _uow.Repository<CardPaie>().ListAsync(c => c.SageBdd == sageDb, ct), request)
            .Where(c => MatchesPointeuse(c.PointeuseBdd, pointeuseDb))
            .Where(c => !string.IsNullOrWhiteSpace(c.SageMatricule))
            .GroupBy(c => MatriculeIdentity.Cle(c.SageMatricule))
            .Select(g => g.OrderBy(c => c.Id).First())
            .ToList();
    }

    private async Task<HashSet<DateTime>> LoadFeriesAsync(CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        return (await _uow.Repository<JourFerie>().ListAsync(f => f.BddSage == sageDb, ct))
            .Where(f => f.Date.HasValue)
            .Select(f => f.Date!.Value.Date)
            .ToHashSet();
    }

    private async Task<Dictionary<int, Dictionary<DateTime, PlanningSalarie>>> LoadPlanningParCarteAsync(
        DateTime from, DateTime to, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        var pointeuseDb = _tenant.PointeuseDb;
        var planning = (await _uow.Repository<PlanningSalarie>().ListAsync(
                p => p.BddSage == sageDb && p.DateP >= from.AddDays(-1) && p.DateP <= to.AddDays(1), ct))
            .Where(p => MatchesPointeuse(p.BddPointeuse, pointeuseDb))
            .Where(p => p.DateP.HasValue);
        return planning
            .GroupBy(p => p.IdSal)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(p => p.DateP!.Value.Date)
                    .ToDictionary(gg => gg.Key, gg => gg.OrderBy(p => p.Id).First()));
    }

    private async Task<Dictionary<string, HashSet<DateTime>>> LoadSemainesValideesAsync(CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        return (await _uow.Repository<HsValidee>().ListAsync(v => v.BddSage == sageDb && v.ValidationHs == true, ct))
            .Where(v => v.DateDeb.HasValue && !string.IsNullOrWhiteSpace(v.Matricules))
            .GroupBy(v => MatriculeIdentity.Cle(v.Matricules))
            .ToDictionary(g => g.Key, g => g.Select(v => v.DateDeb!.Value.Date).ToHashSet());
    }

    private async Task<Dictionary<string, List<PunchMoment>>> LoadPointagesParCleAsync(
        DateTime from, DateTime finExclusive, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        var pointeuseDb = _tenant.PointeuseDb;
        return (await _uow.Repository<Pointage>().ListAsync(
                p => p.NomBddSage == sageDb && p.DatePointage >= from && p.DatePointage < finExclusive, ct))
            .Where(p => MatchesPointeuse(p.NomBddPointeuse, pointeuseDb))
            .Where(p => p.DatePointage.HasValue && p.HeurePointage.HasValue)
            .Where(p => !string.IsNullOrWhiteSpace(p.MatriculeSage))
            .GroupBy(p => MatriculeIdentity.Cle(p.MatriculeSage))
            .ToDictionary(
                g => g.Key,
                g => g.Select(p => new PunchMoment(p.DatePointage!.Value.Date, p.HeurePointage!.Value)).ToList());
    }

    private async Task PersistAsync(PunchProcessingRun run, CancellationToken ct)
    {
        var aSupprimer = run.AnomaliesASupprimer();
        if (aSupprimer.Count > 0)
            _uow.Repository<HeureAnomalie>().RemoveRange(aSupprimer);
        if (run.NouvellesCorrections.Count > 0)
            await _uow.Repository<HeureCorrigee>().AddRangeAsync(run.NouvellesCorrections, ct);
        if (run.NouvellesAnomalies.Count > 0)
            await _uow.Repository<HeureAnomalie>().AddRangeAsync(run.NouvellesAnomalies, ct);
        await _uow.SaveChangesAsync(ct);
    }

    private static PreprocessPunchesResultDto BuildResult(PunchProcessingRun run)
    {
        var corrections = run.NouvellesCorrections.Count;
        var anomalies = run.NouvellesAnomalies.Count;
        var message = $"{corrections} correction(s) créée(s), {anomalies} anomalie(s) à arbitrer"
            + (run.AnomaliesMisesAJour > 0 ? $", {run.AnomaliesMisesAJour} anomalie(s) mise(s) à jour" : string.Empty)
            + (run.JoursIgnores > 0 ? $", {run.JoursIgnores} jour(s) protégé(s) ignoré(s)" : string.Empty)
            + ".";
        return new PreprocessPunchesResultDto(corrections, anomalies, run.JoursIgnores, message);
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
