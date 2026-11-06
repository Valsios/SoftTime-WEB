using System.Linq.Expressions;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public sealed class AnomalyService : IAnomalyService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IHistoryArchiveService _history;
    private readonly IValidatedWeekGuard _validatedWeekGuard;

    public AnomalyService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IHistoryArchiveService history,
        IValidatedWeekGuard validatedWeekGuard)
    {
        _uow = uow;
        _tenant = tenant;
        _history = history;
        _validatedWeekGuard = validatedWeekGuard;
    }

    private static bool AbsenceJourneeComplete(HeureAnomalie anomalie)
        => anomalie.AbsAm == true && anomalie.AbsPm == true;

    public async Task<IReadOnlyList<AnomalyDto>> ListAnomaliesAsync(PeriodRequest filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        // Les anomalies sans date (importées sans date exploitable) ne doivent pas rester invisibles.
        var list = await _uow.Repository<HeureAnomalie>().ListReadOnlyAsync(AnomalyPredicate(filter, _tenant.SageDb), ct);
        return list.Select(MapAnomaly).ToList();
    }

    /// <summary>Liste paginée des anomalies ; les anomalies sans date restent incluses.</summary>
    public async Task<PagedResult<AnomalyDto>> ListAnomaliesPagedAsync(PeriodRequest filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        return await PagedLists.PageAsync(
            _uow.Repository<HeureAnomalie>(),
            AnomalyPredicate(filter, _tenant.SageDb),
            q => q.OrderBy(a => a.DatePointageIn)
                  .ThenBy(a => a.MatriculeSage)
                  .ThenBy(a => a.Id),
            page,
            pageSize,
            MapAnomaly,
            filter.Search,
            new Expression<Func<HeureAnomalie, string?>>[]
            {
                a => a.MatriculeSage,
                a => a.IntituleAbsence,
            },
            ct);
    }

    private static Expression<Func<HeureAnomalie, bool>> AnomalyPredicate(PeriodRequest filter, string sageDb)
    {
        var from = filter.From;
        var to = filter.To;
        return a => a.BddSage == sageDb
            && (a.DatePointageIn == null
                || (a.DatePointageIn >= from && a.DatePointageIn <= to));
    }

    public async Task<AnomalyDto?> GetAnomalyAsync(decimal id, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var entity = await LoadAnomalyAsync(id, ct);
        return entity is null ? null : MapAnomaly(entity);
    }

    /// <summary>
    /// Arbitre une anomalie : renseigne les heures retenues (entrée, pause, sortie) et les drapeaux
    /// d'absence/férié. Portage de <c>CorrectionFerie</c> + <c>CorrectionES</c> du desktop, réunis
    /// dans un seul endpoint (le desktop avait deux endpoints au comportement redondant).
    /// </summary>
    public async Task<AnomalyDto> UpdateAnomalyAsync(decimal id, AnomalyUpdateDto dto, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var entity = await LoadAnomalyAsync(id, ct)
            ?? throw new NotFoundException($"Anomalie introuvable (ID={id}).");

        var dateIn = dto.DateIn?.Date ?? entity.DatePointageIn?.Date;
        var dateOut = dto.DateOut?.Date ?? entity.DatePointageOut?.Date;
        var hEntree = dto.HEntree ?? entity.HEntree;
        var hPs = dto.HPs ?? entity.Hps;
        var hPe = dto.HPe ?? entity.Hpe;
        var hSortie = dto.HSortie ?? entity.HSortie;
        ValidateAnomalyHours(dateIn, hEntree, hPs, hPe, dateOut, hSortie);

        entity.DatePointageIn = dateIn;
        entity.DatePointageOut = dateOut;
        entity.HEntree = hEntree;
        entity.Hps = hPs;
        entity.Hpe = hPe;
        entity.HSortie = hSortie;
        if (dto.AbsAm.HasValue) entity.AbsAm = dto.AbsAm;
        if (dto.AbsPm.HasValue) entity.AbsPm = dto.AbsPm;
        if (dto.Feries.HasValue) entity.Feries = dto.Feries;
        if (dto.FeriesAm.HasValue) entity.FeriesAm = dto.FeriesAm;
        if (dto.FeriesPm.HasValue) entity.FeriesPm = dto.FeriesPm;
        if (dto.IntituleAbsence is not null)
            entity.IntituleAbsence = string.IsNullOrWhiteSpace(dto.IntituleAbsence) ? null : dto.IntituleAbsence.Trim();

        _uow.Repository<HeureAnomalie>().Update(entity);
        await _uow.SaveChangesAsync(ct);
        return MapAnomaly(entity);
    }

    /// <summary>
    /// Valide en masse : chaque anomalie sélectionnée devient une heure corrigée
    /// (<c>ORIGINE = 'MANUEL'</c>, <c>VALIDER_CORRECTION = true</c>) puis l'anomalie est supprimée.
    /// Portage de <c>ValiderCorrections</c> du desktop.
    /// </summary>
    public async Task<AnomalyValidationResultDto> ValidateAnomaliesAsync(
        IReadOnlyCollection<decimal> ids, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        if (ids is null || ids.Count == 0)
            throw new DomainException("Aucune anomalie sélectionnée.");

        var cible = ids.ToHashSet();
        var anomalies = (await _uow.Repository<HeureAnomalie>()
                .ListAsync(a => a.BddSage == _tenant.SageDb, ct))
            .Where(a => cible.Contains(a.Id))
            .ToList();
        if (anomalies.Count == 0)
            throw new DomainException("Aucune anomalie trouvée pour les identifiants fournis.");
        await EnsureValidableAsync(anomalies, ct);

        var corrRepo = _uow.Repository<HeureCorrigee>();
        foreach (var anomalie in anomalies)
            await corrRepo.AddAsync(ToCorrection(anomalie), ct);
        _uow.Repository<HeureAnomalie>().RemoveRange(anomalies);
        await _history.ArchiveAnomaliesAsync(anomalies, ct);
        await _uow.SaveChangesAsync(ct);

        return new AnomalyValidationResultDto(anomalies.Count, anomalies.Count,
            $"{anomalies.Count} anomalie(s) convertie(s) en heure(s) corrigée(s).");
    }

    /// <summary>
    /// Une absence de la journée complète (matin et après-midi) se valide sans heures : c'est
    /// l'arbitrage « le salarié était absent » (des heures saisies en plus seraient payées). Sinon,
    /// entrée et sortie sont obligatoires, et aucune semaine validée ne doit être touchée.
    /// </summary>
    private async Task EnsureValidableAsync(List<HeureAnomalie> anomalies, CancellationToken ct)
    {
        RefuserSi(anomalies,
            a => AbsenceJourneeComplete(a) && (a.HEntree.HasValue || a.HSortie.HasValue),
            detail => $"Absence de la journée complète et heures saisies pour : {detail}. Retirez les heures ou l'absence avant de valider.");
        RefuserSi(anomalies,
            a => !AbsenceJourneeComplete(a) && (a.HEntree is null || a.HSortie is null),
            detail => $"Heure d'entrée ou de sortie manquante pour : {detail}. Renseignez-les avant de valider.");

        var semainesValidees = await _validatedWeekGuard.ChargerSemainesValideesAsync(_tenant.SageDb, ct);
        RefuserSi(anomalies,
            a => a.DatePointageIn.HasValue
                 && semainesValidees.Contains((MatriculeIdentity.Cle(a.MatriculeSage), ValidatedWeeks.Lundi(a.DatePointageIn.Value))),
            detail => $"Semaine validée pour : {detail}. Annulez la validation avant de modifier les heures.");
    }

    /// <summary>Refuse la validation si des anomalies remplissent la condition (cinq citées au plus).</summary>
    private static void RefuserSi(
        List<HeureAnomalie> anomalies, Func<HeureAnomalie, bool> condition, Func<string, string> message)
    {
        var concernees = anomalies.Where(condition).ToList();
        if (concernees.Count == 0)
            return;
        var detail = string.Join(", ", concernees.Take(5)
            .Select(a => $"{a.MatriculeSage} du {a.DatePointageIn?.ToString("dd/MM/yyyy") ?? "?"}"));
        throw new DomainException(message(detail));
    }

    /// <summary>Heure corrigée manuelle et validée reprenant les valeurs arbitrées de l'anomalie.</summary>
    private static HeureCorrigee ToCorrection(HeureAnomalie anomalie) => new()
    {
        BddSage = anomalie.BddSage,
        BddPointeuse = anomalie.BddPointeuse,
        MatriculeSage = anomalie.MatriculeSage,
        DatePointageIn = anomalie.DatePointageIn,
        DatePointageOut = anomalie.DatePointageOut,
        HEntree = anomalie.HEntree,
        Hps = anomalie.Hps,
        Hpe = anomalie.Hpe,
        HSortie = anomalie.HSortie,
        AbsenceAm = anomalie.AbsAm ?? false,
        AbsencePm = anomalie.AbsPm ?? false,
        Ferier = anomalie.Feries,
        FerierAm = anomalie.FeriesAm ?? false,
        FerierPm = anomalie.FeriesPm ?? false,
        IntituleAbsence = anomalie.IntituleAbsence,
        ValiderCorrection = true,
        Origine = PunchProcessingConfiguration.OrigineManuel
    };

    public async Task DeleteAnomalyAsync(decimal id, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var repo = _uow.Repository<HeureAnomalie>();
        var e = await LoadAnomalyAsync(id, ct) ?? throw new NotFoundException();
        await _history.ArchiveAnomaliesAsync(new[] { e }, ct);
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    /// <summary>Charge une anomalie en la restreignant à la base SAGE du tenant courant.</summary>
    private async Task<HeureAnomalie?> LoadAnomalyAsync(decimal id, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        return (await _uow.Repository<HeureAnomalie>()
            .ListAsync(a => a.Id == id && a.BddSage == sageDb, ct)).FirstOrDefault();
    }

    /// <summary>
    /// Cohérence des heures arbitrées : la sortie doit être postérieure à l'entrée et, le même jour,
    /// les heures de pause doivent être comprises entre les deux (portage de la validation desktop).
    /// </summary>
    private static void ValidateAnomalyHours(
        DateTime? dateIn, TimeSpan? hEntree, TimeSpan? hPs, TimeSpan? hPe, DateTime? dateOut, TimeSpan? hSortie)
    {
        if (!dateIn.HasValue || !dateOut.HasValue || !hEntree.HasValue || !hSortie.HasValue)
            return;

        var entree = dateIn.Value.Date.Add(hEntree.Value);
        var sortie = dateOut.Value.Date.Add(hSortie.Value);
        if (sortie <= entree)
            throw new DomainException("La sortie doit être postérieure à l'entrée.");

        if (dateIn.Value.Date != dateOut.Value.Date)
            return;
        foreach (var pause in new[] { hPs, hPe }.Where(h => h.HasValue).Select(h => h!.Value))
        {
            var t = dateIn.Value.Date.Add(pause);
            if (t < entree || t > sortie)
                throw new DomainException(
                    "Les heures de pause doivent être comprises entre l'entrée et la sortie.");
        }
    }

    private static AnomalyDto MapAnomaly(HeureAnomalie a) => new(
        a.Id, a.MatriculeSage, a.DatePointageIn, a.DatePointageOut, a.H1, a.H2,
        a.HEntree, a.Hps, a.Hpe, a.HSortie,
        a.AbsAm, a.AbsPm, a.Feries, a.FeriesAm, a.FeriesPm, a.IntituleAbsence);
}
