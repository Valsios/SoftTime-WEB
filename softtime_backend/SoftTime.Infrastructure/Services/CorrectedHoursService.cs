using System.Linq.Expressions;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public sealed class CorrectedHoursService : ICorrectedHoursService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IValidatedWeekGuard _validatedWeekGuard;

    public CorrectedHoursService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IValidatedWeekGuard validatedWeekGuard)
    {
        _uow = uow;
        _tenant = tenant;
        _validatedWeekGuard = validatedWeekGuard;
    }

    public async Task<IReadOnlyList<CorrectedHourDto>> ListCorrectionsAsync(PeriodRequest filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var list = await _uow.Repository<HeureCorrigee>().ListReadOnlyAsync(CorrectionPredicate(filter, _tenant.SageDb), ct);
        return list.Select(MapCorrection).ToList();
    }

    /// <summary>
    /// Liste paginée des heures corrigées (pagination SQL, voir <see cref="ListPunchesPagedAsync"/> :
    /// l'endpoint non paginé reste inchangé pour ne pas tronquer l'export Excel du client).
    /// </summary>
    public async Task<PagedResult<CorrectedHourDto>> ListCorrectionsPagedAsync(PeriodRequest filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        return await PagedLists.PageAsync(
            _uow.Repository<HeureCorrigee>(),
            CorrectionPredicate(filter, _tenant.SageDb),
            q => q.OrderBy(a => a.DatePointageIn)
                  .ThenBy(a => a.MatriculeSage)
                  .ThenBy(a => a.Id),
            page,
            pageSize,
            MapCorrection,
            filter.Search,
            new Expression<Func<HeureCorrigee, string?>>[]
            {
                a => a.MatriculeSage,
                a => a.IntituleAbsence,
            },
            ct);
    }

    private static Expression<Func<HeureCorrigee, bool>> CorrectionPredicate(PeriodRequest filter, string sageDb)
    {
        var from = filter.From;
        var to = filter.To;
        var matriculeFrom = filter.MatriculeFrom;
        var matriculeTo = filter.MatriculeTo;
        return a => a.BddSage == sageDb
            && a.DatePointageIn >= from
            && a.DatePointageIn <= to
            && (string.IsNullOrEmpty(matriculeFrom) || string.Compare(a.MatriculeSage, matriculeFrom) >= 0)
            && (string.IsNullOrEmpty(matriculeTo) || string.Compare(a.MatriculeSage, matriculeTo) <= 0);
    }

    public async Task<CorrectedHourDto> SaveCorrectionAsync(CorrectedHourDto dto, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var repo = _uow.Repository<HeureCorrigee>();
        var sageDb = _tenant.SageDb;
        // La semaine cible ne doit pas être validée (ni, pour une modification, la semaine d'origine).
        await _validatedWeekGuard.RefuserSiSemaineValideeAsync(sageDb, dto.Matricule, dto.DateIn, ct);
        HeureCorrigee entity;
        if (dto.Id == 0)
        {
            entity = new HeureCorrigee
            {
                BddSage = _tenant.SageDb,
                BddPointeuse = _tenant.PointeuseDb,
                MatriculeSage = dto.Matricule,
                // Une saisie manuelle est protégée du pré-traitement des pointages.
                Origine = PunchProcessingConfiguration.OrigineManuel
            };
            Apply(dto, entity);
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new NotFoundException();
            await _validatedWeekGuard.RefuserSiSemaineValideeAsync(sageDb, entity.MatriculeSage, entity.DatePointageIn, ct);
            entity.Origine = PunchProcessingConfiguration.OrigineManuel;
            Apply(dto, entity);
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return MapCorrection(entity);
    }

    public async Task DeleteCorrectionAsync(decimal id, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var repo = _uow.Repository<HeureCorrigee>();
        var sageDb = _tenant.SageDb;
        var e = (await repo.ListAsync(h => h.Id == id && h.BddSage == sageDb, ct)).FirstOrDefault()
                ?? throw new NotFoundException();
        await _validatedWeekGuard.RefuserSiSemaineValideeAsync(sageDb, e.MatriculeSage, e.DatePointageIn, ct);
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<ImportResultDto> ImportCorrectionsExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var semainesValidees = await _validatedWeekGuard.ChargerSemainesValideesAsync(_tenant.SageDb, ct);
        var (imported, sansMatricule, verrouillees) = (0, 0, 0);
        foreach (var row in rows)
        {
            var correction = LireCorrection(row);
            if (correction is null)
            {
                sansMatricule++;
                continue;
            }
            // Une semaine validée est figée : la ligne est ignorée plutôt que d'en changer les heures.
            if (correction.DatePointageIn is DateTime dIn
                && semainesValidees.Contains((MatriculeIdentity.Cle(correction.MatriculeSage), ValidatedWeeks.Lundi(dIn))))
            {
                verrouillees++;
                continue;
            }
            correction.BddSage = _tenant.SageDb;
            correction.BddPointeuse = _tenant.PointeuseDb;
            await _uow.Repository<HeureCorrigee>().AddAsync(correction, ct);
            imported++;
        }
        await _uow.SaveChangesAsync(ct);
        return ImportResult(imported, sansMatricule, verrouillees);
    }

    /// <summary>
    /// Ligne Excel en correction manuelle validée (protégée du pré-traitement des pointages), ou
    /// <c>null</c> sans matricule.
    /// </summary>
    private static HeureCorrigee? LireCorrection(Dictionary<string, string> row)
    {
        var matr = ExcelCells.Get(row, "Matricule", "MATRICULE", "Matricule SAGE");
        if (string.IsNullOrWhiteSpace(matr))
            return null;
        ExcelCells.TryDate(ExcelCells.Get(row, "DateEntree", "DATE ENTREE", "Date entrée", "Date in"), out var dIn);
        ExcelCells.TryDate(ExcelCells.Get(row, "DateSortie", "DATE SORTIE", "Date sortie", "Date out"), out var dOut);
        ExcelCells.TryTime(ExcelCells.Get(row, "HeureEntree", "HEURE ENTREE", "Heure entrée"), out var hIn);
        ExcelCells.TryTime(ExcelCells.Get(row, "HeureSortie", "HEURE SORTIE", "Heure sortie"), out var hOut);
        return new HeureCorrigee
        {
            MatriculeSage = matr,
            DatePointageIn = dIn == default ? null : dIn,
            DatePointageOut = dOut == default ? null : dOut,
            HEntree = hIn,
            HSortie = hOut,
            ValiderCorrection = true,
            Origine = PunchProcessingConfiguration.OrigineManuel
        };
    }

    private static ImportResultDto ImportResult(int imported, int sansMatricule, int verrouillees)
    {
        var skipped = sansMatricule + verrouillees;
        var message = $"{imported} correction(s) importée(s), {skipped} ligne(s) ignorée(s)";
        var motifs = new List<string>();
        if (sansMatricule > 0)
            motifs.Add("matricule manquant");
        if (verrouillees > 0)
            motifs.Add($"{verrouillees} sur une semaine validée");
        if (motifs.Count > 0)
            message += $" ({string.Join(", ", motifs)})";
        return new ImportResultDto(imported, skipped, message + ".");
    }

    private static void Apply(CorrectedHourDto dto, HeureCorrigee e)
    {
        e.MatriculeSage = dto.Matricule;
        e.DatePointageIn = dto.DateIn;
        e.DatePointageOut = dto.DateOut;
        e.HEntree = dto.HEntree;
        e.Hps = dto.HPs;
        e.Hpe = dto.HPe;
        e.HSortie = dto.HSortie;
        e.AbsenceAm = dto.AbsAm;
        e.AbsencePm = dto.AbsPm;
        e.IntituleAbsence = dto.IntituleAbsence;
        e.Retard = dto.Retard;
        e.Ferier = dto.Ferie;
        e.FerierAm = dto.FerieAm;
        e.FerierPm = dto.FeriePm;
        e.ValiderCorrection = dto.ValiderCorrection;
        e.Hs = dto.Hs;
        e.MNuit = dto.MNuit;
        e.MDimanche = dto.MDimanche;
        e.MFeries = dto.MFeries;
    }

    private static CorrectedHourDto MapCorrection(HeureCorrigee a) => new(
        a.Id, a.MatriculeSage, a.DatePointageIn, a.DatePointageOut, a.HEntree, a.Hps, a.Hpe, a.HSortie,
        a.AbsenceAm, a.AbsencePm, a.IntituleAbsence, a.Retard, a.Ferier, a.FerierAm, a.FerierPm,
        a.ValiderCorrection, a.ValiderHs, a.Hs, a.MNuit, a.MDimanche, a.MFeries);
}
