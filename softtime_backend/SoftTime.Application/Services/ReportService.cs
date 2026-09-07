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
    private readonly ISageContextFactory _sageFactory;

    public ReportService(IUnitOfWork uow, TenantConnectionService tenant, ISageContextFactory sageFactory)
    {
        _uow = uow;
        _tenant = tenant;
        _sageFactory = sageFactory;
    }

    public async Task<IReadOnlyList<CorrectedHourDto>> PointageAsync(ReportFilter filter, CancellationToken ct = default)
    {
        var list = await _uow.Repository<T_HEURECORRIGER>().ListAsync(h =>
            h.BDD_SAGE == _tenant.SageDb
            && h.DATE_POINTAGE_IN >= filter.From && h.DATE_POINTAGE_IN <= filter.To
            && (string.IsNullOrEmpty(filter.MatriculeFrom) || string.Compare(h.MATRICULE_SAGE, filter.MatriculeFrom) >= 0)
            && (string.IsNullOrEmpty(filter.MatriculeTo) || string.Compare(h.MATRICULE_SAGE, filter.MatriculeTo) <= 0), ct);
        return list.Select(a => new CorrectedHourDto(a.ID, a.MATRICULE_SAGE, a.DATE_POINTAGE_IN, a.DATE_POINTAGE_OUT, a.HENTREE, a.HPS, a.HPE, a.HSORTIE,
            a.ABSENCE_AM, a.ABSENCE_PM, a.INTITULE_ABSENCE, a.RETARD, a.FERIER, a.FERIER_AM, a.FERIER_PM, a.VALIDER_CORRECTION, a.VALIDER_HS, a.HS, a.M_NUIT, a.M_DIMANCHE, a.M_FERIES)).ToList();
    }

    public async Task<IReadOnlyList<object>> AbsencesAsync(ReportFilter filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var list = await _uow.Repository<T_HEURECORRIGER>().ListAsync(h =>
            h.BDD_SAGE == _tenant.SageDb && h.VALIDER_CORRECTION == true
            && (h.ABSENCE_AM == true || h.ABSENCE_PM == true)
            && h.DATE_POINTAGE_IN >= filter.From && h.DATE_POINTAGE_IN <= filter.To, ct);
        var print = _uow.Repository<T_impr_ABS>();
        var old = await print.ListAsync(_ => true, ct);
        print.RemoveRange(old);
        var result = new List<object>();
        foreach (var h in list.Where(x => x.DATE_POINTAGE_IN.HasValue))
        {
            var d = h.DATE_POINTAGE_IN!.Value;
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
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
            result.Add(new { h.MATRICULE_SAGE, Date = d, h.ABSENCE_AM, h.ABSENCE_PM, h.INTITULE_ABSENCE });
        }
        await _uow.SaveChangesAsync(ct);
        return result;
    }

    public async Task<IReadOnlyList<object>> RetardsAsync(ReportFilter filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var list = await _uow.Repository<T_HEURECORRIGER>().ListAsync(h =>
            h.BDD_SAGE == _tenant.SageDb && h.RETARD != null && h.RETARD > TimeSpan.Zero
            && h.DATE_POINTAGE_IN >= filter.From && h.DATE_POINTAGE_IN <= filter.To, ct);
        var print = _uow.Repository<T_impr_RET>();
        var old = await print.ListAsync(_ => true, ct);
        print.RemoveRange(old);
        var result = new List<object>();
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
            result.Add(new { h.MATRICULE_SAGE, h.DATE_POINTAGE_IN, h.HENTREE, h.RETARD });
        }
        await _uow.SaveChangesAsync(ct);
        return result;
    }

    public async Task<IReadOnlyList<HsExoDto>> HsRecapAsync(ReportFilter filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var list = await _uow.Repository<T_HSExoImp>().ListAsync(h =>
            h.BDD_SAGE == _tenant.SageDb && h.PeriodeDebut == filter.From.Date && h.PeriodeFin == filter.To.Date, ct);
        return list.Select(h => new HsExoDto(h.OID, h.Matricule, h.NumSal, h.EXO130, h.EXO150, h.I130, h.I150, h.FERIE, h.NUIT, h.DIM, h.RETARD, h.ABSENCE)).ToList();
    }

    public async Task<IReadOnlyList<object>> HeuresParSemaineAsync(ReportFilter filter, CancellationToken ct = default)
    {
        if (!OvertimeRules.IsMondayToSundayWeek(filter.From, filter.To))
            throw new InvalidOperationException("La semaine doit aller du lundi au dimanche.");
        var hs = await _uow.Repository<T_HS_VALIDER>().ListAsync(h =>
            h.BDD_SAGE == _tenant.SageDb && h.DATE_DEB == filter.From.Date && h.DATE_FIN == filter.To.Date
            && (string.IsNullOrEmpty(filter.MatriculeFrom) || string.Compare(h.MATRICULES, filter.MatriculeFrom) >= 0)
            && (string.IsNullOrEmpty(filter.MatriculeTo) || string.Compare(h.MATRICULES, filter.MatriculeTo) <= 0), ct);
        return hs.Select(h =>
        {
            var split = OvertimeRules.SplitWeekly(h.HS ?? 0);
            return (object)new
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
        }).ToList();
    }

    public async Task<IReadOnlyList<object>> HeuresDimancheAsync(ReportFilter filter, CancellationToken ct = default)
    {
        var list = await _uow.Repository<T_HEUREDIMANCHE>().ListAsync(h =>
            h.BDD_SAGE == _tenant.SageDb && h.Date_Dim >= filter.From && h.Date_Dim <= filter.To, ct);
        return list.Select(h => (object)new { h.MATRICULE_SAGE, h.Date_Dim, h.HeureDebut, h.HeureFin, h.Valeur }).ToList();
    }

    public async Task<AbsenceInfo?> GetLeaveAsync(string matricule, DateTime date, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        using var sage = _sageFactory.Create(await _tenant.GetSageConnectionAsync(ct));
        var excluded = new[] { "0200", "0300" };
        var codes = sage.Events.Where(e => !excluded.Contains(e.CodeNE)).Select(e => e.CodeNE).ToList();
        var emp = sage.Employees.FirstOrDefault(e => e.MatriculeSalarie == matricule);
        if (emp == null) return null;
        var events = sage.EmployeeEvents.Where(e => e.NumSalarie == emp.SA_CompteurNumero && codes.Contains(e.CodeNE)).ToList();
        foreach (var ghrs in events)
        {
            if (!ghrs.PeriodeDebut.HasValue || !ghrs.PeriodeFin.HasValue) continue;
            var days = (int)ghrs.PeriodeFin.Value.Subtract(ghrs.PeriodeDebut.Value).TotalDays;
            for (var i = 0; i <= days; i++)
            {
                var d = ghrs.PeriodeDebut.Value.Date.AddDays(i);
                if (d.DayOfWeek == DayOfWeek.Sunday) continue;
                if (d != date.Date) continue;
                var valeur = (ghrs.Matin == 1 || ghrs.ApresMidi == 1) && days == 0 ? 0.5 : 1;
                return new AbsenceInfo { Date = d, Evenement = ghrs.CodeNE, Matricule = matricule, Valeur = valeur };
            }
        }
        return null;
    }
}
