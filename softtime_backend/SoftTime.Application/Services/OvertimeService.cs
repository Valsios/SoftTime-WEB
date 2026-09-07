using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

public class OvertimeService
{
    private readonly IUnitOfWork _uow;
    private readonly TenantConnectionService _tenant;
    private readonly ISageContextFactory _sageFactory;
    private readonly ISagePayrollWriter _payroll;
    private readonly CatalogService _catalog;

    public OvertimeService(IUnitOfWork uow, TenantConnectionService tenant, ISageContextFactory sageFactory, ISagePayrollWriter payroll, CatalogService catalog)
    {
        _uow = uow;
        _tenant = tenant;
        _sageFactory = sageFactory;
        _payroll = payroll;
        _catalog = catalog;
    }

    public async Task<IReadOnlyList<WeeklyHsDto>> PreviewWeekAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        EnsureMondayToSunday(request);
        return await ComputeWeekAsync(request, persistNonValidated: true, ct);
    }

    public async Task ValidateWeekAsync(WeeklyValidationRequest request, bool validate, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var selected = new HashSet<string>(
            (request.SelectedMatricules ?? Array.Empty<string>()).Where(m => !string.IsNullOrWhiteSpace(m)),
            StringComparer.OrdinalIgnoreCase);
        var rows = await ComputeWeekAsync(request, persistNonValidated: true, ct);
        var v = _uow.Repository<T_HS_VALIDER>();
        var corr = _uow.Repository<T_HEURECORRIGER>();
        foreach (var row in rows)
        {
            var isSelected = row.Matricule != null && selected.Contains(row.Matricule);
            if (!validate && !isSelected)
                continue;
            var flag = validate && isSelected;
            var existing = (await v.ListAsync(x =>
                x.MATRICULES == row.Matricule && x.DATE_DEB == request.From.Date && x.BDD_SAGE == _tenant.SageDb, ct)).FirstOrDefault();
            if (validate && existing?.VALIDATION_HS == true)
                continue;
            if (existing != null)
            {
                existing.VALIDATION_HS = flag;
                existing.DATE_FIN = request.To.Date;
                existing.HT = row.Ht;
                existing.HS = row.Hs;
                existing.NUIT = row.Nuit;
                existing.DIMANCHE = row.Dimanche;
                existing.FERIES = row.Feries;
                existing.RETARD = row.Retard;
                existing.ABSENCE = row.Absence;
                v.Update(existing);
            }
            else
            {
                await v.AddAsync(new T_HS_VALIDER
                {
                    BDD_SAGE = _tenant.SageDb,
                    BDD_POINTAGE = _tenant.PointeuseDb,
                    MATRICULES = row.Matricule,
                    DATE_DEB = request.From.Date,
                    DATE_FIN = request.To.Date,
                    HT = row.Ht,
                    HS = row.Hs,
                    NUIT = row.Nuit,
                    DIMANCHE = row.Dimanche,
                    FERIES = row.Feries,
                    RETARD = row.Retard,
                    ABSENCE = row.Absence,
                    VALIDATION_HS = flag
                }, ct);
            }
            var hours = await corr.ListAsync(h =>
                h.MATRICULE_SAGE == row.Matricule && h.BDD_SAGE == _tenant.SageDb
                && h.DATE_POINTAGE_IN >= request.From && h.DATE_POINTAGE_IN <= request.To, ct);
            foreach (var h in hours)
            {
                h.VALIDER_HS = flag;
                corr.Update(h);
            }
        }
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<HsExoDto>> ListExoAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        EnsureMondayToSunday(request);
        var list = await _uow.Repository<T_HSExoImp>().ListAsync(h =>
            h.BDD_SAGE == _tenant.SageDb
            && h.PeriodeDebut <= request.To.Date
            && h.PeriodeFin >= request.From.Date
            && (string.IsNullOrEmpty(request.MatriculeFrom) || string.Compare(h.Matricule, request.MatriculeFrom) >= 0)
            && (string.IsNullOrEmpty(request.MatriculeTo) || string.Compare(h.Matricule, request.MatriculeTo) <= 0), ct);
        return list
            .OrderBy(h => h.Matricule)
            .Select(h => new HsExoDto(h.OID, h.Matricule, h.NumSal, h.EXO130, h.EXO150, h.I130, h.I150, h.FERIE, h.NUIT, h.DIM, h.RETARD, h.ABSENCE))
            .ToList();
    }

    public async Task<IReadOnlyList<HsExoDto>> CalculateExoImpoAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        EnsureMondayToSunday(request);
        var cards = await TargetCards(request, ct);
        var numSalByMat = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var sage = _sageFactory.Create(await _tenant.GetSageConnectionAsync(ct));
            foreach (var emp in sage.Employees.ToArray())
            {
                var mat = emp.MatriculeSalarie?.Trim();
                if (!string.IsNullOrEmpty(mat))
                    numSalByMat[mat] = emp.SA_CompteurNumero;
            }
        }
        catch { /* SAGE optional for preview */ }
        var result = new List<HsExoDto>();
        foreach (var card in cards)
        {
            var weeks = await _uow.Repository<T_HSSemaine>().ListAsync(h =>
                h.Matricule == card.SAGE_MATRICULE
                && h.PeriodeDebut >= request.From.Date
                && h.PeriodeDebut <= request.To.Date
                && h.BDD_SAGE == _tenant.SageDb, ct);
            List<HsWeekSplit> splits;
            if (weeks.Count == 0)
            {
                var validated = await _uow.Repository<T_HS_VALIDER>().ListAsync(h =>
                    h.MATRICULES == card.SAGE_MATRICULE && h.BDD_SAGE == _tenant.SageDb
                    && h.DATE_DEB >= request.From && h.DATE_FIN <= request.To, ct);
                splits = new List<HsWeekSplit>();
                var i = 1;
                foreach (var w in validated.OrderBy(x => x.DATE_DEB))
                {
                    var split = OvertimeRules.SplitWeekly(w.HS ?? 0);
                    splits.Add(split);
                    await UpsertHsSemaine(card.SAGE_MATRICULE ?? string.Empty, i++, w.DATE_DEB ?? request.From, w.DATE_FIN ?? request.To, request.From, request.To, split, ct);
                }
            }
            else
            {
                splits = weeks.OrderBy(w => w.Semaine)
                    .Select(w => new HsWeekSplit { Hs = w.HS ?? 0, Hs1 = w.HS1 ?? 0, Hs2 = w.HS2 ?? 0 })
                    .ToList();
            }
            var exo = OvertimeRules.AllocateExempt(splits);
            var validatedWeeks = await _uow.Repository<T_HS_VALIDER>().ListAsync(h =>
                h.MATRICULES == card.SAGE_MATRICULE && h.BDD_SAGE == _tenant.SageDb
                && h.DATE_DEB >= request.From && h.DATE_FIN <= request.To, ct);
            var nuit = validatedWeeks.Sum(x => x.NUIT ?? 0);
            var dim = validatedWeeks.Sum(x => x.DIMANCHE ?? 0);
            var ferie = validatedWeeks.Sum(x => x.FERIES ?? 0);
            var retard = validatedWeeks.Sum(x => x.RETARD ?? 0);
            var repo = _uow.Repository<T_HSExoImp>();
            var old = await repo.ListAsync(h => h.Matricule == card.SAGE_MATRICULE && h.PeriodeDebut == request.From.Date && h.PeriodeFin == request.To.Date && h.BDD_SAGE == _tenant.SageDb, ct);
            repo.RemoveRange(old);
            int? numSal = null;
            var matKey = card.SAGE_MATRICULE?.Trim();
            if (!string.IsNullOrEmpty(matKey) && numSalByMat.TryGetValue(matKey, out var n))
                numSal = n;
            var entity = new T_HSExoImp
            {
                Matricule = card.SAGE_MATRICULE,
                NumSal = numSal,
                EXO130 = exo.Exo130,
                EXO150 = exo.Exo150,
                I130 = exo.Impo130,
                I150 = exo.Impo150,
                PeriodeDebut = request.From.Date,
                PeriodeFin = request.To.Date,
                NUIT = nuit,
                DIM = dim,
                FERIE = ferie,
                RETARD = retard,
                BDD_SAGE = _tenant.SageDb,
                BDD_POINTEUSE = _tenant.PointeuseDb
            };
            await repo.AddAsync(entity, ct);
            result.Add(new HsExoDto(entity.OID, entity.Matricule, entity.NumSal, entity.EXO130, entity.EXO150, entity.I130, entity.I150, entity.FERIE, entity.NUIT, entity.DIM, entity.RETARD, entity.ABSENCE));
        }
        await _uow.SaveChangesAsync(ct);
        return result;
    }

    public async Task PurgeHsAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        EnsureMondayToSunday(request);
        var sem = await _uow.Repository<T_HSSemaine>().ListAsync(h =>
            h.BDD_SAGE == _tenant.SageDb && h.PeriodeDebut == request.From.Date && h.PeriodeFin == request.To.Date
            && (string.IsNullOrEmpty(request.MatriculeFrom) || string.Compare(h.Matricule, request.MatriculeFrom) >= 0)
            && (string.IsNullOrEmpty(request.MatriculeTo) || string.Compare(h.Matricule, request.MatriculeTo) <= 0), ct);
        var exo = await _uow.Repository<T_HSExoImp>().ListAsync(h =>
            h.BDD_SAGE == _tenant.SageDb && h.PeriodeDebut == request.From.Date && h.PeriodeFin == request.To.Date
            && (string.IsNullOrEmpty(request.MatriculeFrom) || string.Compare(h.Matricule, request.MatriculeFrom) >= 0)
            && (string.IsNullOrEmpty(request.MatriculeTo) || string.Compare(h.Matricule, request.MatriculeTo) <= 0), ct);
        _uow.Repository<T_HSSemaine>().RemoveRange(sem);
        _uow.Repository<T_HSExoImp>().RemoveRange(exo);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task SyncSageAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        EnsureMondayToSunday(request);
        var rows = await _uow.Repository<T_HSExoImp>().ListAsync(h =>
            h.BDD_SAGE == _tenant.SageDb && h.PeriodeDebut == request.From.Date && h.PeriodeFin == request.To.Date, ct);
        using var sage = _sageFactory.Create(await _tenant.GetSageConnectionAsync(ct));
        var constants = sage.Constants.ToArray();
        var conn = await _tenant.GetSageConnectionAsync(ct);
        var codes = await _catalog.GetOvertimeSageCodesAsync(ct);
        foreach (var hs in rows.Where(h => h.NumSal.HasValue))
        {
            await _payroll.SyncOvertimeAsync(conn, constants, hs.NumSal!.Value, codes,
                hs.EXO130 ?? 0, hs.EXO150 ?? 0, hs.I130 ?? 0, hs.I150 ?? 0,
                hs.FERIE ?? 0, hs.NUIT ?? 0, hs.DIM ?? 0, ct);
        }
    }

    private async Task<List<WeeklyHsDto>> ComputeWeekAsync(PeriodRequest request, bool persistNonValidated, CancellationToken ct)
    {
        var cards = await TargetCards(request, ct);
        var feries = await _uow.Repository<T_FERIE>().ListAsync(f => f.BDD_SAGE == _tenant.SageDb, ct);
        var cats = await _uow.Repository<T_CATEGORIE>().ListAsync(c => c.BDD_SAGE == _tenant.SageDb, ct);
        var affect = await _uow.Repository<T_CAT_SAL>().ListAsync(a => a.BDD_SAGE == _tenant.SageDb, ct);
        var tols = await _uow.Repository<T_TOLERANCE>().ListAsync(_ => true, ct);
        var hsRepo = _uow.Repository<T_HS_VALIDER>();
        var result = new List<WeeklyHsDto>();
        foreach (var card in cards)
        {
            var existing = (await hsRepo.ListAsync(x =>
                x.MATRICULES == card.SAGE_MATRICULE && x.DATE_DEB == request.From.Date && x.BDD_SAGE == _tenant.SageDb, ct)).FirstOrDefault();
            if (existing?.VALIDATION_HS == true)
                continue;

            var hours = await _uow.Repository<T_HEURECORRIGER>().ListAsync(h =>
                h.MATRICULE_SAGE == card.SAGE_MATRICULE && h.BDD_SAGE == _tenant.SageDb
                && h.DATE_POINTAGE_IN >= request.From.Date && h.DATE_POINTAGE_IN <= request.To.Date, ct);
            var planning = await _uow.Repository<T_PLANNING_SAL>().ListAsync(p =>
                p.IDSAL == card.ID && p.BDD_SAGE == _tenant.SageDb && p.DateP >= request.From.Date && p.DateP <= request.To.Date, ct);
            var aff = affect.FirstOrDefault(a => a.MATRICULE_SAGE == card.ID);
            var cat = aff == null ? null : cats.FirstOrDefault(c => c.IDCATEGORIE == aff.ID_CATEG);
            var weeklyNeed = cat?.HEURESEMAINE ?? 40;
            var pause = cat?.PAUSE ?? TimeSpan.Zero;
            decimal ht = 0, nuit = 0, dim = 0, ferieH = 0, retard = 0, absence = 0;
            foreach (var day in Enumerable.Range(0, 7).Select(i => request.From.Date.AddDays(i)))
            {
                var plan = planning.FirstOrDefault(p => p.DateP == day);
                var corr = hours.FirstOrDefault(h => h.DATE_POINTAGE_IN.HasValue && h.DATE_POINTAGE_IN.Value.Date == day);
                var isFerie = feries.Any(f => f.DATE.HasValue && f.DATE.Value.Date == day);
                if (corr?.HENTREE != null && corr.HSORTIE != null)
                {
                    var start = corr.HENTREE.Value;
                    if (plan?.HA != null && start < plan.HA.Value.TimeOfDay)
                        start = plan.HA.Value.TimeOfDay;
                    var worked = corr.HSORTIE.Value - start - pause;
                    if (worked < TimeSpan.Zero) worked = TimeSpan.Zero;
                    ht += (decimal)worked.TotalHours;
                    if (start.Hours < 6 || corr.HSORTIE.Value.Hours >= 21)
                        nuit += (decimal)Math.Max(0, worked.TotalHours);
                    if (day.DayOfWeek == DayOfWeek.Sunday)
                        dim += (decimal)worked.TotalHours;
                    if (isFerie)
                        ferieH += (decimal)worked.TotalHours;
                    if (corr.RETARD.HasValue)
                        retard += (decimal)corr.RETARD.Value.TotalHours;
                }
                else if (plan is { OffP: not true, HA: not null, HD: not null })
                {
                    var abs = plan.HD.Value.TimeOfDay - plan.HA.Value.TimeOfDay - pause;
                    if (abs > TimeSpan.Zero)
                        absence += (decimal)abs.TotalHours;
                }
                if (corr?.RETARD == null && plan?.HA != null && corr?.HENTREE != null)
                {
                    var tol = tols.FirstOrDefault(t => cat != null && t.IDCATEGORIE == cat.IDCATEGORIE && t.TYPES_TOL == false);
                    var allowed = plan.HA.Value.TimeOfDay.Add(TimeSpan.FromMinutes(tol?.TOLERANCE ?? 0));
                    if (corr.HENTREE > allowed)
                        retard += (decimal)(corr.HENTREE.Value - allowed).TotalHours;
                }
            }
            var hs = Math.Max(0, ht - weeklyNeed);
            if (persistNonValidated)
            {
                if (existing != null)
                {
                    existing.BDD_POINTAGE = _tenant.PointeuseDb;
                    existing.DATE_FIN = request.To.Date;
                    existing.HT = ht;
                    existing.HS = hs;
                    existing.NUIT = nuit;
                    existing.DIMANCHE = dim;
                    existing.FERIES = ferieH;
                    existing.RETARD = retard;
                    existing.ABSENCE = absence;
                    existing.VALIDATION_HS = false;
                    hsRepo.Update(existing);
                }
                else
                {
                    existing = new T_HS_VALIDER
                    {
                        BDD_SAGE = _tenant.SageDb,
                        BDD_POINTAGE = _tenant.PointeuseDb,
                        MATRICULES = card.SAGE_MATRICULE,
                        DATE_DEB = request.From.Date,
                        DATE_FIN = request.To.Date,
                        HT = ht,
                        HS = hs,
                        NUIT = nuit,
                        DIMANCHE = dim,
                        FERIES = ferieH,
                        RETARD = retard,
                        ABSENCE = absence,
                        VALIDATION_HS = false
                    };
                    await hsRepo.AddAsync(existing, ct);
                }
            }
            result.Add(new WeeklyHsDto(existing?.ID ?? 0, card.SAGE_MATRICULE, request.From.Date, request.To.Date, ht, hs, nuit, dim, ferieH, retard, absence, false));
        }
        await _uow.SaveChangesAsync(ct);
        return result;
    }

    private static void EnsureMondayToSunday(PeriodRequest request)
    {
        if (!OvertimeRules.IsMondayToSundayWeek(request.From, request.To))
            throw new InvalidOperationException("La période doit être un lundi–dimanche (7 jours).");
    }

    private async Task<List<T_CARDPAIE>> TargetCards(PeriodRequest request, CancellationToken ct)
    {
        var cards = await _uow.Repository<T_CARDPAIE>().ListAsync(c =>
            c.SAGE_BDD == _tenant.SageDb
            && (string.IsNullOrEmpty(request.Branche) || c.BRANCHE == request.Branche)
            && (string.IsNullOrEmpty(request.MatriculeFrom) || string.Compare(c.SAGE_MATRICULE, request.MatriculeFrom) >= 0)
            && (string.IsNullOrEmpty(request.MatriculeTo) || string.Compare(c.SAGE_MATRICULE, request.MatriculeTo) <= 0), ct);
        return cards;
    }

    private async Task UpsertHsSemaine(string matricule, int noSem, DateTime debutSem, DateTime finSem, DateTime debutPeriode, DateTime finPeriode, HsWeekSplit split, CancellationToken ct)
    {
        var repo = _uow.Repository<T_HSSemaine>();
        var old = await repo.ListAsync(h => h.Matricule == matricule && h.Semaine == noSem && h.PeriodeDebut == debutPeriode.Date && h.BDD_SAGE == _tenant.SageDb, ct);
        repo.RemoveRange(old);
        await repo.AddAsync(new T_HSSemaine
        {
            Matricule = matricule,
            TotalHeure = split.Hs,
            HS = split.Hs,
            HS1 = split.Hs1,
            HS2 = split.Hs2,
            PeriodeDebut = debutPeriode.Date,
            PeriodeFin = finPeriode.Date,
            Semaine = noSem,
            BDD_SAGE = _tenant.SageDb,
            BDD_POINTEUSE = _tenant.PointeuseDb
        }, ct);
    }
}
