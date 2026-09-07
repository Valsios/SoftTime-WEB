using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Application.Services;

public class TimekeepingService
{
    private readonly IUnitOfWork _uow;
    private readonly TenantConnectionService _tenant;
    private readonly IPointeuseContextFactory _pointeuseFactory;

    public TimekeepingService(IUnitOfWork uow, TenantConnectionService tenant, IPointeuseContextFactory pointeuseFactory)
    {
        _uow = uow;
        _tenant = tenant;
        _pointeuseFactory = pointeuseFactory;
    }

    public async Task<IReadOnlyList<PunchDto>> ListPunchesAsync(PeriodRequest filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var list = await _uow.Repository<T_POINTAGE>().ListAsync(p =>
            p.NOM_BDD_SAGE == _tenant.SageDb
            && p.DATE_POINTAGE >= filter.From.Date
            && p.DATE_POINTAGE <= filter.To.Date.AddDays(1)
            && (string.IsNullOrEmpty(filter.MatriculeFrom) || string.Compare(p.MATRICULE_SAGE, filter.MatriculeFrom) >= 0)
            && (string.IsNullOrEmpty(filter.MatriculeTo) || string.Compare(p.MATRICULE_SAGE, filter.MatriculeTo) <= 0), ct);
        return list.Select(p => new PunchDto(p.IDPOINTAGE, p.MATRICULE_SAGE, p.DATE_POINTAGE, p.HEURE_POINTAGE, p.TYPE_POINTAGE, p.DATE_IMPORTATION)).ToList();
    }

    public async Task<ImportResultDto> ImportFromClockAsync(ImportPunchesRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var clock = (await _uow.Repository<T_CLOCK>().ListAsync(_ => true, ct)).FirstOrDefault()
            ?? throw new InvalidOperationException("Paramètre pointeuse (T_CLOCK) manquant.");
        var pteRow = await _tenant.GetPointeuseRowAsync(ct);
        using var pte = _pointeuseFactory.Create(await _tenant.GetPointeuseConnectionAsync(ct));
        var cards = await _uow.Repository<T_CARDPAIE>().ListAsync(c => c.SAGE_BDD == _tenant.SageDb, ct);
        if (!string.IsNullOrEmpty(request.MatriculeFrom))
            cards = cards.Where(c => string.Compare(c.SAGE_MATRICULE, request.MatriculeFrom, StringComparison.Ordinal) >= 0).ToList();
        if (!string.IsNullOrEmpty(request.MatriculeTo))
            cards = cards.Where(c => string.Compare(c.SAGE_MATRICULE, request.MatriculeTo, StringComparison.Ordinal) <= 0).ToList();
        var badgeMap = cards.Where(c => !string.IsNullOrEmpty(c.POINTEUSE_NUMERO)).ToDictionary(c => c.POINTEUSE_NUMERO!.Trim(), c => c, StringComparer.OrdinalIgnoreCase);
        var users = pte.Users.ToList();
        var from = request.From.Date;
        var to = request.To.Date.AddDays(1);
        var punches = pte.CheckInOut.Where(c => c.CHECKTIME >= from && c.CHECKTIME < to).ToList();
        var repo = _uow.Repository<T_POINTAGE>();
        var existing = await repo.ListAsync(p =>
            p.NOM_BDD_SAGE == _tenant.SageDb && p.DATE_POINTAGE >= from && p.DATE_POINTAGE < to, ct);
        var imported = 0;
        var skipped = 0;
        foreach (var punch in punches)
        {
            var user = users.FirstOrDefault(u => u.USERID == punch.USERID);
            if (user == null || !badgeMap.TryGetValue(user.BADGENUMBER.Trim(), out var card))
            { skipped++; continue; }
            var dup = existing.Any(e =>
                e.MATRICULE_SAGE == card.SAGE_MATRICULE
                && e.DATE_POINTAGE.HasValue && e.DATE_POINTAGE.Value.Date == punch.CHECKTIME.Date
                && e.HEURE_POINTAGE.HasValue && e.HEURE_POINTAGE.Value.Hours == punch.CHECKTIME.Hour
                && e.HEURE_POINTAGE.Value.Minutes == punch.CHECKTIME.Minute);
            if (dup && clock.MULTIPOINT != true)
            { skipped++; continue; }
            await repo.AddAsync(new T_POINTAGE
            {
                NOM_BDD_SAGE = _tenant.SageDb,
                NOM_BDD_POINTEUSE = pteRow.NOM_BD,
                MATRICULE_SAGE = card.SAGE_MATRICULE,
                DATE_POINTAGE = punch.CHECKTIME.Date,
                HEURE_POINTAGE = punch.CHECKTIME.TimeOfDay,
                DATE_IMPORTATION = DateTime.Now,
                TYPE_POINTAGE = string.IsNullOrWhiteSpace(punch.CHECKTYPE) ? pteRow.TYPE_POINTAGE : punch.CHECKTYPE,
                IDUNIQUE_POINTAGE = punch.USERID
            }, ct);
            imported++;
        }
        await _uow.SaveChangesAsync(ct);
        return new ImportResultDto(imported, skipped, "Import pointeuse terminé.");
    }

    public async Task<ImportResultDto> ImportPunchesExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var repo = _uow.Repository<T_POINTAGE>();
        var imported = 0;
        var skipped = 0;
        foreach (var row in rows)
        {
            var matr = ExcelCells.Get(row, "Matricule", "MATRICULE", "Matricule SAGE");
            if (string.IsNullOrWhiteSpace(matr)) { skipped++; continue; }
            if (!ExcelCells.TryDate(ExcelCells.Get(row, "Date", "DATE", "Date pointage"), out var date))
            { skipped++; continue; }
            ExcelCells.TryTime(ExcelCells.Get(row, "Heure", "HEURE", "Heure pointage"), out var heure);
            var dup = (await repo.ListAsync(p =>
                p.NOM_BDD_SAGE == _tenant.SageDb
                && p.MATRICULE_SAGE == matr
                && p.DATE_POINTAGE != null
                && p.DATE_POINTAGE.Value.Date == date.Date
                && p.HEURE_POINTAGE == heure, ct)).Count > 0;
            if (dup)
            {
                skipped++;
                continue;
            }
            await repo.AddAsync(new T_POINTAGE
            {
                MATRICULE_SAGE = matr,
                DATE_POINTAGE = date.Date,
                HEURE_POINTAGE = heure,
                DATE_IMPORTATION = DateTime.Now,
                NOM_BDD_SAGE = _tenant.SageDb,
                NOM_BDD_POINTEUSE = _tenant.PointeuseDb,
                TYPE_POINTAGE = "IO"
            }, ct);
            imported++;
        }
        await _uow.SaveChangesAsync(ct);
        return new ImportResultDto(imported, skipped, "Import Excel pointages terminé.");
    }

    public async Task<IReadOnlyList<AnomalyDto>> ListAnomaliesAsync(PeriodRequest filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var list = await _uow.Repository<T_HEUREANOMALIE>().ListAsync(a =>
            a.BDD_SAGE == _tenant.SageDb
            && a.DATE_POINTAGE_IN >= filter.From
            && a.DATE_POINTAGE_IN <= filter.To, ct);
        return list.Select(a => new AnomalyDto(a.ID, a.MATRICULE_SAGE, a.DATE_POINTAGE_IN, a.DATE_POINTAGE_OUT, a.H1, a.H2, a.HENTREE, a.HSORTIE, a.ABS_AM, a.ABS_PM, a.FERIES_AM, a.FERIES_PM, a.INTITULE_ABSENCE)).ToList();
    }

    public async Task DeleteAnomalyAsync(decimal id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_HEUREANOMALIE>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CorrectedHourDto>> ListCorrectionsAsync(PeriodRequest filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var list = await _uow.Repository<T_HEURECORRIGER>().ListAsync(a =>
            a.BDD_SAGE == _tenant.SageDb
            && a.DATE_POINTAGE_IN >= filter.From
            && a.DATE_POINTAGE_IN <= filter.To
            && (string.IsNullOrEmpty(filter.MatriculeFrom) || string.Compare(a.MATRICULE_SAGE, filter.MatriculeFrom) >= 0)
            && (string.IsNullOrEmpty(filter.MatriculeTo) || string.Compare(a.MATRICULE_SAGE, filter.MatriculeTo) <= 0), ct);
        return list.Select(MapCorrection).ToList();
    }

    public async Task<CorrectedHourDto> SaveCorrectionAsync(CorrectedHourDto dto, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var repo = _uow.Repository<T_HEURECORRIGER>();
        T_HEURECORRIGER entity;
        if (dto.Id == 0)
        {
            entity = new T_HEURECORRIGER
            {
                BDD_SAGE = _tenant.SageDb,
                BDD_POINTEUSE = _tenant.PointeuseDb,
                MATRICULE_SAGE = dto.Matricule
            };
            Apply(dto, entity);
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
            Apply(dto, entity);
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return MapCorrection(entity);
    }

    public async Task DeleteCorrectionAsync(decimal id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_HEURECORRIGER>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<ImportResultDto> ImportCorrectionsExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var repo = _uow.Repository<T_HEURECORRIGER>();
        var imported = 0;
        foreach (var row in rows)
        {
            var matr = ExcelCells.Get(row, "Matricule", "MATRICULE", "Matricule SAGE");
            if (string.IsNullOrWhiteSpace(matr)) continue;
            ExcelCells.TryDate(ExcelCells.Get(row, "DateEntree", "DATE ENTREE", "Date entrée", "Date in"), out var dIn);
            ExcelCells.TryDate(ExcelCells.Get(row, "DateSortie", "DATE SORTIE", "Date sortie", "Date out"), out var dOut);
            ExcelCells.TryTime(ExcelCells.Get(row, "HeureEntree", "HEURE ENTREE", "Heure entrée"), out var hIn);
            ExcelCells.TryTime(ExcelCells.Get(row, "HeureSortie", "HEURE SORTIE", "Heure sortie"), out var hOut);
            await repo.AddAsync(new T_HEURECORRIGER
            {
                MATRICULE_SAGE = matr,
                DATE_POINTAGE_IN = dIn == default ? null : dIn,
                DATE_POINTAGE_OUT = dOut == default ? null : dOut,
                HENTREE = hIn,
                HSORTIE = hOut,
                BDD_SAGE = _tenant.SageDb,
                BDD_POINTEUSE = _tenant.PointeuseDb,
                VALIDER_CORRECTION = true
            }, ct);
            imported++;
        }
        await _uow.SaveChangesAsync(ct);
        return new ImportResultDto(imported, 0, "Import corrections terminé.");
    }

    private static void Apply(CorrectedHourDto dto, T_HEURECORRIGER e)
    {
        e.MATRICULE_SAGE = dto.Matricule;
        e.DATE_POINTAGE_IN = dto.DateIn;
        e.DATE_POINTAGE_OUT = dto.DateOut;
        e.HENTREE = dto.HEntree;
        e.HPS = dto.HPs;
        e.HPE = dto.HPe;
        e.HSORTIE = dto.HSortie;
        e.ABSENCE_AM = dto.AbsAm;
        e.ABSENCE_PM = dto.AbsPm;
        e.INTITULE_ABSENCE = dto.IntituleAbsence;
        e.RETARD = dto.Retard;
        e.FERIER = dto.Ferie;
        e.FERIER_AM = dto.FerieAm;
        e.FERIER_PM = dto.FeriePm;
        e.VALIDER_CORRECTION = dto.ValiderCorrection;
        e.HS = dto.Hs;
        e.M_NUIT = dto.MNuit;
        e.M_DIMANCHE = dto.MDimanche;
        e.M_FERIES = dto.MFeries;
    }

    private static CorrectedHourDto MapCorrection(T_HEURECORRIGER a) => new(
        a.ID, a.MATRICULE_SAGE, a.DATE_POINTAGE_IN, a.DATE_POINTAGE_OUT, a.HENTREE, a.HPS, a.HPE, a.HSORTIE,
        a.ABSENCE_AM, a.ABSENCE_PM, a.INTITULE_ABSENCE, a.RETARD, a.FERIER, a.FERIER_AM, a.FERIER_PM,
        a.VALIDER_CORRECTION, a.VALIDER_HS, a.HS, a.M_NUIT, a.M_DIMANCHE, a.M_FERIES);
}
