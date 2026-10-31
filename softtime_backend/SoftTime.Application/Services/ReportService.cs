using System.Linq.Expressions;
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
    /// État des absences <b>non justifiées</b> : croisement planning / corrections / congés SAGE /
    /// fériés. Les règles (<see cref="AbsenceReportRules"/>) calculent les trois natures d'absence,
    /// mais l'état n'affiche que les journées planifiées sans aucun pointage. Les congés SAGE, les
    /// jours fériés, les week-ends, les jours OFF et les absences déclarées
    /// (<c>ABSENCE_AM</c> / <c>ABSENCE_PM</c>) servent uniquement à exclure le jour.
    /// </summary>
    public async Task<IReadOnlyList<AbsenceReportRow>> AbsencesAsync(ReportFilter filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var ctx = await ChargerContexteAsync(filter, ct);
        if (ctx is null)
            return Array.Empty<AbsenceReportRow>();

        return AbsenceReportRules.Calculer(
                ctx.Plannings,
                ctx.Cartes,
                ctx.Corrections,
                ctx.Conges,
                ctx.Feries,
                filter.From.Date,
                filter.To.Date,
                DateTime.Today)
            .Where(r => r.Type == AbsenceReportRules.TypeNonJustifiee)
            .ToList();
    }

    /// <summary>
    /// État absences paginé. Délègue au calcul complet puis découpe en mémoire.
    /// TODO (phase adaptation) : remplacer par un vrai découpage SQL sur AbsenceReportRules.
    /// </summary>
    public async Task<PagedResult<object>> AbsencesPagedAsync(
        ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var rows = (await AbsencesAsync(filter, ct)).Cast<object>().ToList();
        return PagedLists.Page(rows, page, pageSize);
    }

    /// <summary>
    /// État retards : calcul complet (première entrée du jour vs planning + tolérance, comme le
    /// tableau de bord) puis écriture de <c>T_impr_RET</c> pour l'impression.
    /// </summary>
    public async Task<IReadOnlyList<RetardReportRow>> RetardsAsync(ReportFilter filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var rows = await CalculerRetardsAsync(filter, ct);
        await WriteRetardsPrintAsync(rows, ct);
        return rows;
    }

    /// <summary>État retards paginé, sans écriture de la table d'impression.</summary>
    public async Task<PagedResult<object>> RetardsPagedAsync(
        ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var rows = await CalculerRetardsAsync(filter, ct);
        return PagedLists.Page(rows.Cast<object>().ToList(), page, pageSize);
    }

    /// <summary>Retards calculés de la période et du périmètre (mêmes règles que le dashboard).</summary>
    private async Task<IReadOnlyList<RetardReportRow>> CalculerRetardsAsync(ReportFilter filter, CancellationToken ct)
    {
        var ctx = await ChargerContexteAsync(filter, ct);
        if (ctx is null)
            return Array.Empty<RetardReportRow>();

        return RetardReportRules.Calculer(
            ctx.Cartes,
            ctx.Corrections,
            ctx.Plannings,
            ctx.CategorieParCarte,
            ctx.Tolerances,
            ctx.Feries,
            filter.From.Date,
            filter.To.Date,
            DateTime.Today);
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
    public async Task<PagedResult<object>> PagedAsync(
        string type, ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        if (SearchText.Terms(filter.Search).Count > 0)
        {
            IReadOnlyList<object> rows = type switch
            {
                "pointage" => (await PointageAsync(filter, ct)).Cast<object>().ToList(),
                "absences" => (await AbsencesAsync(filter, ct)).Cast<object>().ToList(),
                "retards" => (await CalculerRetardsAsync(filter, ct)).Cast<object>().ToList(),
                "hs-recap" => (await HsRecapAsync(filter, ct)).Cast<object>().ToList(),
                "heures-semaine" => (await HeuresParSemaineAsync(filter, ct)).ToList(),
                "heures-dimanche" => (await HeuresDimancheAsync(filter, ct)).ToList(),
                _ => throw new ArgumentException($"Type de rapport inconnu : {type}.")
            };
            var filtered = rows
                .Where(row => SearchText.MatchesObject(filter.Search, row))
                .ToList();
            return PagedLists.Page(filtered, page, pageSize);
        }

        return type switch
        {
            "pointage" => await PointagePagedAsync(filter, page, pageSize, ct),
            "absences" => await AbsencesPagedAsync(filter, page, pageSize, ct),
            "retards" => await RetardsPagedAsync(filter, page, pageSize, ct),
            "hs-recap" => await HsRecapPagedAsync(filter, page, pageSize, ct),
            "heures-semaine" => await HeuresSemainePagedAsync(filter, page, pageSize, ct),
            "heures-dimanche" => await HeuresDimanchePagedAsync(filter, page, pageSize, ct),
            _ => throw new ArgumentException($"Type de rapport inconnu : {type}.")
        };
    }

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

    /// <summary>
    /// Contexte partagé par l'état des absences et l'état des retards : cartes filtrées (base SAGE,
    /// plage de matricules, département), fériés, congés SAGE, corrections, plannings, affectations de
    /// catégorie et tolérances. La fenêtre est plafonnée à la veille : aucun état ne fabrique
    /// d'événement futur, exactement comme le tableau de bord.
    /// </summary>
    private async Task<ReportContext?> ChargerContexteAsync(ReportFilter filter, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        var from = filter.From.Date;
        var hier = DateTime.Today.AddDays(-1);
        var fin = filter.To.Date < hier ? filter.To.Date : hier;
        if (from > fin)
            return null;

        var borneFinExclusive = fin.AddDays(1);

        // 1. Cartes filtrées par tenant SAGE, plage de matricules (MatriculeRange) et Département
        var periodRequest = new PeriodRequest(filter.MatriculeFrom?.Trim(), filter.MatriculeTo?.Trim(), filter.From, filter.To, filter.Departement);
        var cartes = (await _uow.Repository<T_CARDPAIE>().ListAsync(c => c.SAGE_BDD == sageDb, ct))
            .Where(c => MatriculeRange.Contains(c.SAGE_MATRICULE?.Trim(), periodRequest))
            .Where(c => string.IsNullOrWhiteSpace(filter.Departement)
                        || string.Equals(c.DEPARTEMENT?.Trim(), filter.Departement.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (cartes.Count == 0)
            return null;

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
            _logger.LogWarning(ex, "Impossible de charger les congés SAGE pour les rapports (base: {SageDb}).", sageDb);
        }

        // 4. Corrections de la plage [from, veille] restreintes aux salariés retenus (C5)
        var corrections = (await _uow.Repository<T_HEURECORRIGER>().ListAsync(h =>
            h.BDD_SAGE == sageDb
            && h.DATE_POINTAGE_IN >= from && h.DATE_POINTAGE_IN < borneFinExclusive, ct))
            .Where(h => clesCartes.Contains(MatriculeIdentity.Cle(h.MATRICULE_SAGE)))
            .ToList();

        // 5. Plannings de la plage [from, veille]. Les jours OFF sont conservés ici : ils sont
        // interprétés par les règles (un jour OFF n'est jamais une absence ni un retard).
        var plannings = (await _uow.Repository<T_PLANNING_SAL>().ListAsync(p =>
            p.BDD_SAGE == sageDb
            && p.DateP >= from && p.DateP < borneFinExclusive, ct))
            .Where(p => idsalSet.Contains(p.IDSAL))
            .ToList();

        // 6. Catégorie du salarié (T_CAT_SAL référence T_CARDPAIE.ID) et tolérances de catégorie
        var categorieParCarte = (await _uow.Repository<T_CAT_SAL>().ListAsync(a => a.BDD_SAGE == sageDb, ct))
            .GroupBy(a => a.MATRICULE_SAGE)
            .ToDictionary(g => g.Key, g => g.First().ID_CATEG);
        var tolerances = await _uow.Repository<T_TOLERANCE>().ListAsync(_ => true, ct);

        return new ReportContext(cartes, feries, conges, corrections, plannings, categorieParCarte, tolerances);
    }

    /// <summary>Données chargées une fois pour les états calculés (absences et retards).</summary>
    private sealed record ReportContext(
        IReadOnlyList<T_CARDPAIE> Cartes,
        IReadOnlySet<DateTime> Feries,
        IReadOnlyDictionary<string, List<LeaveDay>> Conges,
        IReadOnlyList<T_HEURECORRIGER> Corrections,
        IReadOnlyList<T_PLANNING_SAL> Plannings,
        IReadOnlyDictionary<int, int> CategorieParCarte,
        IReadOnlyList<T_TOLERANCE> Tolerances);

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

    /// <summary>
    /// Recopie les retards calculés dans la table tampon d'impression <c>T_impr_RET</c>
    /// (équivalent du bouton « Générer état » du desktop). La table est purgée avant réinsertion.
    /// </summary>
    private async Task WriteRetardsPrintAsync(IReadOnlyList<RetardReportRow> rows, CancellationToken ct)
    {
        var print = _uow.Repository<T_impr_RET>();
        var old = await print.ListAsync(_ => true, ct);
        print.RemoveRange(old);
        foreach (var row in rows)
        {
            await print.AddAsync(new T_impr_RET
            {
                BDD_SAGE = _tenant.SageDb,
                BDD_POINTEUSE = _tenant.PointeuseDb,
                MATRICULES = row.Matricule,
                DATE_POINTAGE = row.Date.ToString("dd/MM/yyyy"),
                HEURE_ENTREE = row.HeureEntree,
                RETARDS = TimeSpan.FromMinutes(row.MinutesRetard)
            }, ct);
        }
        await _uow.SaveChangesAsync(ct);
    }

    private static CorrectedHourDto ToCorrectedHourDto(T_HEURECORRIGER a) => new(
        a.ID, a.MATRICULE_SAGE, a.DATE_POINTAGE_IN, a.DATE_POINTAGE_OUT, a.HENTREE, a.HPS, a.HPE, a.HSORTIE,
        a.ABSENCE_AM, a.ABSENCE_PM, a.INTITULE_ABSENCE, a.RETARD, a.FERIER, a.FERIER_AM, a.FERIER_PM,
        a.VALIDER_CORRECTION, a.VALIDER_HS, a.HS, a.M_NUIT, a.M_DIMANCHE, a.M_FERIES);

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
