using Microsoft.Extensions.Logging;
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
    private readonly SageLeaveCalendar _leaves;
    private readonly ILogger<ReportService> _logger;

    public ReportService(
        IUnitOfWork uow,
        TenantConnectionService tenant,
        IExternalSourceService externalSource,
        SageLeaveCalendar leaves,
        ILogger<ReportService> logger)
    {
        _uow = uow;
        _tenant = tenant;
        _externalSource = externalSource;
        _leaves = leaves;
        _logger = logger;
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

    public async Task<IReadOnlyList<AbsenceReportRow>> AbsencesAsync(ReportFilter filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var from = filter.From.Date;
        var to = filter.To.Date;
        var dateDuJour = DateTime.Today;
        var hier = dateDuJour.AddDays(-1);
        var finGenerale = to < hier ? to : hier;

        if (from > finGenerale)
            return Array.Empty<AbsenceReportRow>();

        var borneFinExclusive = finGenerale.AddDays(1);

        // 1. Cartes filtrées par tenant SAGE, plage de matricules (MatriculeRange) et Branche (C5)
        var periodRequest = new PeriodRequest(filter.MatriculeFrom, filter.MatriculeTo, filter.From, filter.To, filter.Branche);
        var cartes = (await _uow.Repository<T_CARDPAIE>().ListAsync(c => c.SAGE_BDD == sageDb, ct))
            .Where(c => MatriculeRange.Contains(c.SAGE_MATRICULE, periodRequest))
            .Where(c => string.IsNullOrWhiteSpace(filter.Branche)
                        || string.Equals(c.BRANCHE, filter.Branche, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (cartes.Count == 0)
            return Array.Empty<AbsenceReportRow>();

        var idsalSet = cartes.Select(c => c.ID).ToHashSet();
        var clesCartes = cartes
            .Select(c => MatriculeIdentity.Cle(c.SAGE_MATRICULE))
            .Where(c => !string.IsNullOrEmpty(c))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 2. Fériés de la base SAGE
        var feries = (await _uow.Repository<T_FERIE>().ListAsync(f => f.BDD_SAGE == sageDb, ct))
            .Where(f => f.DATE.HasValue)
            .Select(f => f.DATE!.Value.Date)
            .ToHashSet();

        // 3. Congés SAGE chargés une seule fois pour le tenant (C6 : log warning si indisponible)
        Dictionary<string, List<LeaveDay>> conges = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            conges = await _leaves.LoadAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Impossible de charger les congés SAGE pour le rapport d'absence (base: {SageDb}).", sageDb);
        }

        // 4. Corrections de la plage [From, finGenerale] restreintes aux salariés retenus (C5)
        var corrections = (await _uow.Repository<T_HEURECORRIGER>().ListAsync(h =>
            h.BDD_SAGE == sageDb
            && h.DATE_POINTAGE_IN >= from && h.DATE_POINTAGE_IN < borneFinExclusive, ct))
            .Where(h => clesCartes.Contains(MatriculeIdentity.Cle(h.MATRICULE_SAGE)))
            .ToList();

        // C2 : dernierJourTraite = max des dates lues dans [From, finGenerale] (null si aucune)
        DateTime? dernierJourTraite = corrections
            .Where(h => h.DATE_POINTAGE_IN.HasValue)
            .Select(h => (DateTime?)h.DATE_POINTAGE_IN!.Value.Date)
            .Max();

        // 5. Plannings de la plage [From, finGenerale] restreints aux salariés retenus (C5)
        var plannings = (await _uow.Repository<T_PLANNING_SAL>().ListAsync(p =>
            p.BDD_SAGE == sageDb
            && p.DateP >= from && p.DateP < borneFinExclusive
            && p.OffP != true, ct))
            .Where(p => idsalSet.Contains(p.IDSAL))
            .ToList();

        // 6. Rapprochement et dédoublonnage pur via AbsenceReportRules
        return AbsenceReportRules.Calculer(
            plannings,
            cartes,
            corrections,
            conges,
            feries,
            from,
            to,
            dateDuJour,
            dernierJourTraite);
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
}
