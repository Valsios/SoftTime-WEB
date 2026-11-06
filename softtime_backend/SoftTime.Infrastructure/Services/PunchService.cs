using System.Linq.Expressions;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services;

public sealed class PunchService : IPunchService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IHistoryArchiveService _history;
    private readonly IValidatedWeekGuard _validatedWeekGuard;

    public PunchService(
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

    public async Task<IReadOnlyList<PunchDto>> ListPunchesAsync(PeriodRequest filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var list = await _uow.Repository<Pointage>().ListReadOnlyAsync(PunchPredicate(filter, _tenant.SageDb), ct);
        return list.Select(ToPunchDto).ToList();
    }

    /// <summary>
    /// Liste paginée des pointages. Le filtre, l'ordre et le découpage sont délégués à SQL
    /// (<c>COUNT(*)</c> + <c>ORDER BY … OFFSET/FETCH</c>) : seules les lignes de la page sont
    /// matérialisées. L'endpoint non paginé reste inchangé pour l'export Excel.
    /// </summary>
    public async Task<PagedResult<PunchDto>> ListPunchesPagedAsync(PeriodRequest filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        return await PagedLists.PageAsync(
            _uow.Repository<Pointage>(),
            PunchPredicate(filter, _tenant.SageDb),
            q => q.OrderBy(p => p.DatePointage)
                  .ThenBy(p => p.HeurePointage)
                  .ThenBy(p => p.MatriculeSage)
                  .ThenBy(p => p.IdPointage),
            page,
            pageSize,
            ToPunchDto,
            filter.Search,
            new Expression<Func<Pointage, string?>>[]
            {
                p => p.MatriculeSage,
                p => p.TypePointage,
            },
            ct);
    }

    private static PunchDto ToPunchDto(Pointage p) => new(
        p.IdPointage, p.MatriculeSage, p.DatePointage, p.HeurePointage, p.TypePointage, p.DateImportation);

    private static Expression<Func<Pointage, bool>> PunchPredicate(PeriodRequest filter, string sageDb)
    {
        var from = filter.From.Date;
        var toInclusive = filter.To.Date.AddDays(1);
        var matriculeFrom = filter.MatriculeFrom;
        var matriculeTo = filter.MatriculeTo;
        return p => p.NomBddSage == sageDb
            && p.DatePointage >= from
            && p.DatePointage <= toInclusive
            && (string.IsNullOrEmpty(matriculeFrom) || string.Compare(p.MatriculeSage, matriculeFrom) >= 0)
            && (string.IsNullOrEmpty(matriculeTo) || string.Compare(p.MatriculeSage, matriculeTo) <= 0);
    }

    /// <summary>
    /// Corrige un pointage importé (date, heure, type). Refusé si la semaine est déjà validée.
    /// Le pré-traitement doit ensuite être relancé pour propager la correction.
    /// </summary>
    public async Task<PunchDto> UpdatePunchAsync(decimal id, PunchUpdateDto dto, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var repo = _uow.Repository<Pointage>();
        var entity = (await repo.ListAsync(p => p.IdPointage == id && p.NomBddSage == sageDb, ct)).FirstOrDefault()
            ?? throw new NotFoundException($"Pointage introuvable (ID={id}).");

        var jour = dto.Date?.Date ?? entity.DatePointage?.Date;
        await _validatedWeekGuard.RefuserSiSemaineValideeAsync(sageDb, entity.MatriculeSage, jour, ct);

        if (dto.Date.HasValue)
            entity.DatePointage = dto.Date.Value.Date;
        if (dto.Heure.HasValue)
            entity.HeurePointage = dto.Heure.Value;
        if (dto.Type is not null)
            entity.TypePointage = string.IsNullOrWhiteSpace(dto.Type) ? null : dto.Type.Trim();

        repo.Update(entity);
        await _uow.SaveChangesAsync(ct);
        return new PunchDto(entity.IdPointage, entity.MatriculeSage, entity.DatePointage,
            entity.HeurePointage, entity.TypePointage, entity.DateImportation);
    }

    /// <summary>
    /// Purge une période de traitement : pointages (archivés), corrections automatiques et anomalies
    /// (archivées), pour la plage de matricules demandée. Portage de <c>Purger</c> du desktop, avec
    /// deux garde-fous : la période ne doit être ni validée ni déjà calculée en HS, et les
    /// corrections <b>manuelles</b> sont conservées (le desktop effaçait tout sans distinction).
    /// </summary>
    public async Task<PurgePunchesResultDto> PurgePunchesAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        // Garde indispensable : sans période explicite, FROM/TO valent DateTime.MinValue et la purge
        // porterait sur l'intégralité des pointages.
        if (request.From == default || request.To == default)
            throw new DomainException("La période (from/to) est requise pour purger.");
        if (request.To.Date < request.From.Date)
            throw new DomainException("La date de fin doit être postérieure ou égale à la date de début.");
        await EnsurePurgeableAsync(request.From.Date, request.To.Date, ct);

        var purge = await LoadPurgeAsync(request, ct);
        if (purge.EstVide)
            return new PurgePunchesResultDto(0, 0, 0, purge.ManuellesConservees,
                purge.ManuellesConservees > 0
                    ? $"Aucune donnée à purger. {purge.ManuellesConservees} correction(s) manuelle(s) conservée(s)."
                    : "Aucune donnée à purger sur la période.");

        await _history.ArchivePunchesAsync(purge.Pointages, ct);
        await _history.ArchiveAnomaliesAsync(purge.Anomalies, ct);
        if (purge.Pointages.Count > 0)
            _uow.Repository<Pointage>().RemoveRange(purge.Pointages);
        if (purge.Corrections.Count > 0)
            _uow.Repository<HeureCorrigee>().RemoveRange(purge.Corrections);
        if (purge.Anomalies.Count > 0)
            _uow.Repository<HeureAnomalie>().RemoveRange(purge.Anomalies);
        await _uow.SaveChangesAsync(ct);

        var message = $"{purge.Pointages.Count} pointage(s), {purge.Corrections.Count} correction(s) automatique(s) "
            + $"et {purge.Anomalies.Count} anomalie(s) purgés.";
        if (purge.ManuellesConservees > 0)
            message += $" {purge.ManuellesConservees} correction(s) manuelle(s) conservée(s).";
        return new PurgePunchesResultDto(purge.Pointages.Count, purge.Corrections.Count, purge.Anomalies.Count, purge.ManuellesConservees, message);
    }

    /// <summary>La période ne doit être ni validée ni déjà calculée en HS.</summary>
    private async Task EnsurePurgeableAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        var validees = (await _uow.Repository<HsValidee>().ListAsync(
            v => v.BddSage == sageDb && v.ValidationHs == true && v.DateDeb >= from && v.DateDeb <= to, ct)).Count;
        if (validees > 0)
            throw new DomainException(
                $"La période contient {validees} semaine(s) validée(s) : annulez la validation avant de purger.");

        var hsExistants = (await _uow.Repository<HsExoImp>().ListAsync(
            h => h.BddSage == sageDb && h.PeriodeDebut >= from && h.PeriodeFin <= to, ct)).Count;
        if (hsExistants > 0)
            throw new DomainException(
                "Des calculs EXO/IMPO existent sur la période : purgez les heures supplémentaires avant de purger les pointages.");
    }

    /// <summary>Lignes de la période et de la plage de matricules ; les corrections manuelles sont conservées.</summary>
    private async Task<PunchPurge> LoadPurgeAsync(PeriodRequest request, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        var from = request.From.Date;
        var finExclusive = request.To.Date.AddDays(1);
        var pointages = (await _uow.Repository<Pointage>().ListAsync(
                p => p.NomBddSage == sageDb && p.DatePointage >= from && p.DatePointage <= finExclusive, ct))
            .Where(p => MatriculeRange.Contains(p.MatriculeSage, request))
            .ToList();
        var corrections = (await _uow.Repository<HeureCorrigee>().ListAsync(
                h => h.BddSage == sageDb && h.DatePointageIn >= from && h.DatePointageIn <= finExclusive, ct))
            .Where(h => MatriculeRange.Contains(h.MatriculeSage, request))
            .ToLookup(h => string.Equals(h.Origine, PunchProcessingConfiguration.OrigineManuel, StringComparison.OrdinalIgnoreCase));
        var anomalies = (await _uow.Repository<HeureAnomalie>().ListAsync(
                a => a.BddSage == sageDb && a.DatePointageIn >= from && a.DatePointageIn <= finExclusive, ct))
            .Where(a => MatriculeRange.Contains(a.MatriculeSage, request))
            .ToList();
        return new PunchPurge(pointages, corrections[false].ToList(), anomalies, corrections[true].Count());
    }
}
