using System.Linq.Expressions;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

public class ReportService
{
    private readonly IUnitOfWork _uow;
    private readonly TenantConnectionService _tenant;
    private readonly IExternalSourceService _externalSource;

    public ReportService(IUnitOfWork uow, TenantConnectionService tenant, IExternalSourceService externalSource)
    {
        _uow = uow;
        _tenant = tenant;
        _externalSource = externalSource;
    }

    // ---------------------------------------------------------------- États (lecture paginée)

    public async Task<IReadOnlyList<CorrectedHourDto>> PointageAsync(ReportFilter filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var list = await _uow.Repository<T_HEURECORRIGER>().ListAsync(PointagePredicate(filter, _tenant.SageDb), ct);
        return list.Select(ToCorrectedHourDto).ToList();
    }

    /// <summary>État pointage paginé : filtre/ordre/découpage délégués à SQL.</summary>
    public async Task<PagedResult<object>> PointagePagedAsync(
        ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        return await PagedLists.PageAsync(
            _uow.Repository<T_HEURECORRIGER>(),
            PointagePredicate(filter, _tenant.SageDb),
            q => q.OrderBy(h => h.DATE_POINTAGE_IN)
                  .ThenBy(h => h.MATRICULE_SAGE)
                  .ThenBy(h => h.ID),
            page,
            pageSize,
            h => (object)ToCorrectedHourDto(h),
            ct);
    }

    /// <summary>
    /// État absences : complet + écriture des tables d'impression (<c>T_impr_ABS</c>) pour
    /// l'export/impression. Le filtre week-end est appliqué avant l'écriture.
    /// </summary>
    public async Task<IReadOnlyList<object>> AbsencesAsync(ReportFilter filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var list = await BuildAbsencesAsync(filter, ct);
        await WriteAbsencesPrintAsync(list, ct);
        return list.Select(ToAbsenceRow).ToList();
    }

    /// <summary>
    /// État absences paginé. Ne réécrit <b>pas</b> <c>T_impr_ABS</c> : la lecture d'une page ne doit
    /// pas déclencher d'écriture. Le filtre week-end est appliqué côté serveur avant le découpage.
    /// </summary>
    public async Task<PagedResult<object>> AbsencesPagedAsync(
        ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var list = await BuildAbsencesAsync(filter, ct);
        var rows = list.Select(ToAbsenceRow).ToList();
        return PagedLists.Page(rows, page, pageSize);
    }

    /// <summary>
    /// État retards : complet + écriture de <c>T_impr_RET</c> pour l'export/impression.
    /// </summary>
    public async Task<IReadOnlyList<object>> RetardsAsync(ReportFilter filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var list = await _uow.Repository<T_HEURECORRIGER>().ListAsync(RetardPredicate(filter, _tenant.SageDb), ct);
        await WriteRetardsPrintAsync(list, ct);
        return list.Select(ToRetardRow).ToList();
    }

    /// <summary>État retards paginé, sans écriture de la table d'impression.</summary>
    public async Task<PagedResult<object>> RetardsPagedAsync(
        ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        return await PagedLists.PageAsync(
            _uow.Repository<T_HEURECORRIGER>(),
            RetardPredicate(filter, _tenant.SageDb),
            q => q.OrderBy(h => h.DATE_POINTAGE_IN)
                  .ThenBy(h => h.MATRICULE_SAGE)
                  .ThenBy(h => h.ID),
            page,
            pageSize,
            ToRetardRow,
            ct);
    }

    public async Task<IReadOnlyList<HsExoDto>> HsRecapAsync(ReportFilter filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var list = await _uow.Repository<T_HSExoImp>().ListAsync(HsRecapPredicate(filter, _tenant.SageDb), ct);
        return list.Select(ToHsExoDto).ToList();
    }

    /// <summary>Récap HS EXO/IMPO paginé.</summary>
    public async Task<PagedResult<object>> HsRecapPagedAsync(
        ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        return await PagedLists.PageAsync(
            _uow.Repository<T_HSExoImp>(),
            HsRecapPredicate(filter, _tenant.SageDb),
            q => q.OrderBy(h => h.Matricule).ThenBy(h => h.OID),
            page,
            pageSize,
            h => (object)ToHsExoDto(h),
            ct);
    }

    public async Task<IReadOnlyList<object>> HeuresParSemaineAsync(ReportFilter filter, CancellationToken ct = default)
    {
        EnsureMondayToSunday(filter);
        await _tenant.EnsureAuthorizedAsync(ct);
        var hs = await _uow.Repository<T_HS_VALIDER>().ListAsync(HeuresSemainePredicate(filter, _tenant.SageDb), ct);
        return hs.Select(ToHeuresSemaineRow).ToList();
    }

    /// <summary>Heures par semaine paginées (répartition EXO/IMPO calculée après la page).</summary>
    public async Task<PagedResult<object>> HeuresSemainePagedAsync(
        ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        EnsureMondayToSunday(filter);
        await _tenant.EnsureAuthorizedAsync(ct);
        return await PagedLists.PageAsync(
            _uow.Repository<T_HS_VALIDER>(),
            HeuresSemainePredicate(filter, _tenant.SageDb),
            q => q.OrderBy(h => h.MATRICULES).ThenBy(h => h.ID),
            page,
            pageSize,
            ToHeuresSemaineRow,
            ct);
    }

    public async Task<IReadOnlyList<object>> HeuresDimancheAsync(ReportFilter filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var list = await _uow.Repository<T_HEUREDIMANCHE>().ListAsync(HeuresDimanchePredicate(filter, _tenant.SageDb), ct);
        return list.Select(ToHeuresDimancheRow).ToList();
    }

    /// <summary>Heures dimanche paginées.</summary>
    public async Task<PagedResult<object>> HeuresDimanchePagedAsync(
        ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        return await PagedLists.PageAsync(
            _uow.Repository<T_HEUREDIMANCHE>(),
            HeuresDimanchePredicate(filter, _tenant.SageDb),
            q => q.OrderBy(h => h.Date_Dim).ThenBy(h => h.MATRICULE_SAGE).ThenBy(h => h.ID),
            page,
            pageSize,
            ToHeuresDimancheRow,
            ct);
    }

    /// <summary>Dispatcher du endpoint générique <c>GET /api/reports/{type}/paged</c>.</summary>
    public Task<PagedResult<object>> PagedAsync(
        string type, ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
        => type switch
        {
            "pointage" => PointagePagedAsync(filter, page, pageSize, ct),
            "absences" => AbsencesPagedAsync(filter, page, pageSize, ct),
            "retards" => RetardsPagedAsync(filter, page, pageSize, ct),
            "hs-recap" => HsRecapPagedAsync(filter, page, pageSize, ct),
            "heures-semaine" => HeuresSemainePagedAsync(filter, page, pageSize, ct),
            "heures-dimanche" => HeuresDimanchePagedAsync(filter, page, pageSize, ct),
            _ => throw new ArgumentException($"Type de rapport inconnu : {type}.")
        };

    // ---------------------------------------------------------------- Congés

    public async Task<AbsenceInfo?> GetLeaveAsync(string matricule, DateTime date, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageRow = await _tenant.GetSageRowAsync(ct);
        var excluded = new[] { "0200", "0300" };
        var allEvents = await _externalSource.GetEventsAsync(sageRow, ct);
        var codes = allEvents.Select(e => e.Code)
            .Where(c => !string.IsNullOrWhiteSpace(c) && !excluded.Contains(c))
            .Select(c => c!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var emp = (await _externalSource.GetEmployeesAsync(sageRow, ct))
            .FirstOrDefault(e => string.Equals(e.Matricule, matricule, StringComparison.OrdinalIgnoreCase));
        if (emp == null) return null;
        var events = (await _externalSource.GetEmployeeEventsAsync(sageRow, ct))
            .Where(e => e.EmployeeId == emp.EmployeeId && e.Code is not null && codes.Contains(e.Code))
            .ToList();
        foreach (var ghrs in events)
        {
            if (ghrs.Start is null || ghrs.End is null) continue;
            var days = (int)ghrs.End.Value.Subtract(ghrs.Start.Value).TotalDays;
            for (var i = 0; i <= days; i++)
            {
                var d = ghrs.Start.Value.Date.AddDays(i);
                if (d.DayOfWeek == DayOfWeek.Sunday) continue;
                if (d != date.Date) continue;
                var valeur = (ghrs.Matin == true || ghrs.ApresMidi == true) && days == 0 ? 0.5 : 1;
                return new AbsenceInfo { Date = d, Evenement = ghrs.Code, Matricule = matricule, Valeur = valeur };
            }
        }
        return null;
    }

    // ---------------------------------------------------------------- Prédicats / projections

    private static void EnsureMondayToSunday(ReportFilter filter)
    {
        if (!OvertimeRules.IsMondayToSundayWeek(filter.From, filter.To))
            throw new InvalidOperationException("La semaine doit aller du lundi au dimanche.");
    }

    private static Expression<Func<T_HEURECORRIGER, bool>> PointagePredicate(ReportFilter filter, string sageDb)
    {
        var from = filter.From;
        var to = filter.To;
        var matriculeFrom = filter.MatriculeFrom;
        var matriculeTo = filter.MatriculeTo;
        return h => h.BDD_SAGE == sageDb
            && h.DATE_POINTAGE_IN >= from
            && h.DATE_POINTAGE_IN <= to
            && (string.IsNullOrEmpty(matriculeFrom) || string.Compare(h.MATRICULE_SAGE, matriculeFrom) >= 0)
            && (string.IsNullOrEmpty(matriculeTo) || string.Compare(h.MATRICULE_SAGE, matriculeTo) <= 0);
    }

    private static Expression<Func<T_HEURECORRIGER, bool>> RetardPredicate(ReportFilter filter, string sageDb)
    {
        var from = filter.From;
        var to = filter.To;
        return h => h.BDD_SAGE == sageDb
            && h.RETARD != null
            && h.RETARD > TimeSpan.Zero
            && h.DATE_POINTAGE_IN >= from
            && h.DATE_POINTAGE_IN <= to;
    }

    private static Expression<Func<T_HSExoImp, bool>> HsRecapPredicate(ReportFilter filter, string sageDb)
    {
        var from = filter.From.Date;
        var to = filter.To.Date;
        return h => h.BDD_SAGE == sageDb && h.PeriodeDebut == from && h.PeriodeFin == to;
    }

    private static Expression<Func<T_HS_VALIDER, bool>> HeuresSemainePredicate(ReportFilter filter, string sageDb)
    {
        var from = filter.From.Date;
        var to = filter.To.Date;
        var matriculeFrom = filter.MatriculeFrom;
        var matriculeTo = filter.MatriculeTo;
        return h => h.BDD_SAGE == sageDb
            && h.DATE_DEB == from
            && h.DATE_FIN == to
            && (string.IsNullOrEmpty(matriculeFrom) || string.Compare(h.MATRICULES, matriculeFrom) >= 0)
            && (string.IsNullOrEmpty(matriculeTo) || string.Compare(h.MATRICULES, matriculeTo) <= 0);
    }

    private static Expression<Func<T_HEUREDIMANCHE, bool>> HeuresDimanchePredicate(ReportFilter filter, string sageDb)
    {
        var from = filter.From;
        var to = filter.To;
        return h => h.BDD_SAGE == sageDb && h.Date_Dim >= from && h.Date_Dim <= to;
    }

    private async Task<IReadOnlyList<T_HEURECORRIGER>> BuildAbsencesAsync(ReportFilter filter, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        var from = filter.From;
        var to = filter.To;
        var list = await _uow.Repository<T_HEURECORRIGER>().ListAsync(
            h => h.BDD_SAGE == sageDb
                 && h.VALIDER_CORRECTION == true
                 && (h.ABSENCE_AM == true || h.ABSENCE_PM == true)
                 && h.DATE_POINTAGE_IN >= from
                 && h.DATE_POINTAGE_IN <= to, ct);
        return list
            .Where(h => h.DATE_POINTAGE_IN.HasValue
                        && h.DATE_POINTAGE_IN.Value.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            .ToList();
    }

    private async Task WriteAbsencesPrintAsync(IReadOnlyList<T_HEURECORRIGER> list, CancellationToken ct)
    {
        var print = _uow.Repository<T_impr_ABS>();
        var old = await print.ListAsync(_ => true, ct);
        print.RemoveRange(old);
        foreach (var h in list)
        {
            var d = h.DATE_POINTAGE_IN!.Value;
            await print.AddAsync(new T_impr_ABS
            {
                BDD_SAGE = _tenant.SageDb,
                BDD_POINTEUSE = _tenant.PointeuseDb,
                MATRICULES = h.MATRICULE_SAGE,
                DATE_POINTAGE = d.ToString("dd/MM/yyyy"),
                ABSENCE_AM = h.ABSENCE_AM,
                ABSENCE_PM = h.ABSENCE_PM,
                INTITULE_ABS = h.INTITULE_ABSENCE
            }, ct);
        }
        await _uow.SaveChangesAsync(ct);
    }

    private async Task WriteRetardsPrintAsync(IReadOnlyList<T_HEURECORRIGER> list, CancellationToken ct)
    {
        var print = _uow.Repository<T_impr_RET>();
        var old = await print.ListAsync(_ => true, ct);
        print.RemoveRange(old);
        foreach (var h in list)
        {
            await print.AddAsync(new T_impr_RET
            {
                BDD_SAGE = _tenant.SageDb,
                BDD_POINTEUSE = _tenant.PointeuseDb,
                MATRICULES = h.MATRICULE_SAGE,
                DATE_POINTAGE = h.DATE_POINTAGE_IN?.ToString("dd/MM/yyyy"),
                HEURE_ENTREE = h.HENTREE,
                RETARDS = h.RETARD
            }, ct);
        }
        await _uow.SaveChangesAsync(ct);
    }

    private static CorrectedHourDto ToCorrectedHourDto(T_HEURECORRIGER a) => new(
        a.ID, a.MATRICULE_SAGE, a.DATE_POINTAGE_IN, a.DATE_POINTAGE_OUT, a.HENTREE, a.HPS, a.HPE, a.HSORTIE,
        a.ABSENCE_AM, a.ABSENCE_PM, a.INTITULE_ABSENCE, a.RETARD, a.FERIER, a.FERIER_AM, a.FERIER_PM,
        a.VALIDER_CORRECTION, a.VALIDER_HS, a.HS, a.M_NUIT, a.M_DIMANCHE, a.M_FERIES);

    private static object ToAbsenceRow(T_HEURECORRIGER h) => new
    {
        h.MATRICULE_SAGE,
        Date = h.DATE_POINTAGE_IN!.Value,
        h.ABSENCE_AM,
        h.ABSENCE_PM,
        h.INTITULE_ABSENCE
    };

    private static object ToRetardRow(T_HEURECORRIGER h) => new
    {
        h.MATRICULE_SAGE,
        h.DATE_POINTAGE_IN,
        h.HENTREE,
        h.RETARD
    };

    private static HsExoDto ToHsExoDto(T_HSExoImp h) => new(
        h.OID, h.Matricule, h.NumSal, h.EXO130, h.EXO150, h.I130, h.I150,
        h.FERIE, h.NUIT, h.DIM, h.RETARD, h.ABSENCE);

    private static object ToHeuresSemaineRow(T_HS_VALIDER h)
    {
        var split = OvertimeRules.SplitWeekly(h.HS ?? 0);
        return new
        {
            h.MATRICULES,
            h.HT,
            h.HS,
            HS130 = split.Hs1,
            HS150 = split.Hs2,
            h.NUIT,
            h.DIMANCHE,
            h.FERIES,
            h.RETARD
        };
    }

    private static object ToHeuresDimancheRow(T_HEUREDIMANCHE h) => new
    {
        h.MATRICULE_SAGE,
        h.Date_Dim,
        h.HeureDebut,
        h.HeureFin,
        h.Valeur
    };
}
