using System.Linq.Expressions;

using Microsoft.Extensions.Logging;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public class ReportService : IReportService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IExternalSourceService _externalSource;
    private readonly ISageLeaveCalendar _leaves;
    private readonly ILogger<ReportService> _logger;

    public ReportService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IExternalSourceService externalSource,
        ISageLeaveCalendar leaves,
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
        var list = await _uow.Repository<HeureCorrigee>().ListReadOnlyAsync(PointagePredicate(filter, _tenant.SageDb), ct);
        return list.Select(ToCorrectedHourDto).ToList();
    }

    /// <summary>État pointage paginé : filtre/ordre/découpage délégués à SQL.</summary>
    public async Task<PagedResult<object>> PointagePagedAsync(
        ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        return await PagedLists.PageAsync(
            _uow.Repository<HeureCorrigee>(),
            PointagePredicate(filter, _tenant.SageDb),
            q => q.OrderBy(h => h.DatePointageIn)
                  .ThenBy(h => h.MatriculeSage)
                  .ThenBy(h => h.Id),
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
        var list = await _uow.Repository<HsExoImp>().ListReadOnlyAsync(HsRecapPredicate(filter, _tenant.SageDb), ct);
        return list.Select(ToHsExoDto).ToList();
    }

    /// <summary>Récap HS EXO/IMPO paginé.</summary>
    public async Task<PagedResult<object>> HsRecapPagedAsync(
        ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        return await PagedLists.PageAsync(
            _uow.Repository<HsExoImp>(),
            HsRecapPredicate(filter, _tenant.SageDb),
            q => q.OrderBy(h => h.Matricule).ThenBy(h => h.Oid),
            page,
            pageSize,
            h => (object)ToHsExoDto(h),
            ct);
    }

    public async Task<IReadOnlyList<object>> HeuresParSemaineAsync(ReportFilter filter, CancellationToken ct = default)
    {
        EnsureMondayToSunday(filter);
        await _tenant.EnsureAuthorizedAsync(ct);
        var hs = await _uow.Repository<HsValidee>().ListReadOnlyAsync(HeuresSemainePredicate(filter, _tenant.SageDb), ct);
        return hs.Select(ToHeuresSemaineRow).ToList();
    }

    /// <summary>Heures par semaine paginées (répartition EXO/IMPO calculée après la page).</summary>
    public async Task<PagedResult<object>> HeuresSemainePagedAsync(
        ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        EnsureMondayToSunday(filter);
        await _tenant.EnsureAuthorizedAsync(ct);
        return await PagedLists.PageAsync(
            _uow.Repository<HsValidee>(),
            HeuresSemainePredicate(filter, _tenant.SageDb),
            q => q.OrderBy(h => h.Matricules).ThenBy(h => h.Id),
            page,
            pageSize,
            ToHeuresSemaineRow,
            ct);
    }

    public async Task<IReadOnlyList<object>> HeuresDimancheAsync(ReportFilter filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var list = await _uow.Repository<HeureDimanche>().ListReadOnlyAsync(HeuresDimanchePredicate(filter, _tenant.SageDb), ct);
        return list.Select(ToHeuresDimancheRow).ToList();
    }

    /// <summary>Heures dimanche paginées.</summary>
    public async Task<PagedResult<object>> HeuresDimanchePagedAsync(
        ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        return await PagedLists.PageAsync(
            _uow.Repository<HeureDimanche>(),
            HeuresDimanchePredicate(filter, _tenant.SageDb),
            q => q.OrderBy(h => h.DateDim).ThenBy(h => h.MatriculeSage).ThenBy(h => h.Id),
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
                return new AbsenceInfo { Date = d, Evenement = ghrs.Code ?? string.Empty, Matricule = matricule, Valeur = valeur };
            }
        }
        return null;
    }

    // ---------------------------------------------------------------- Prédicats / projections

    private static void EnsureMondayToSunday(ReportFilter filter)
    {
        if (!OvertimeRules.IsMondayToSundayWeek(filter.From, filter.To))
            throw new DomainException("La semaine doit aller du lundi au dimanche.");
    }

    private static Expression<Func<HeureCorrigee, bool>> PointagePredicate(ReportFilter filter, string sageDb)
    {
        var from = filter.From;
        var to = filter.To;
        var matriculeFrom = filter.MatriculeFrom;
        var matriculeTo = filter.MatriculeTo;
        return h => h.BddSage == sageDb
            && h.DatePointageIn >= from
            && h.DatePointageIn <= to
            && (string.IsNullOrEmpty(matriculeFrom) || string.Compare(h.MatriculeSage, matriculeFrom) >= 0)
            && (string.IsNullOrEmpty(matriculeTo) || string.Compare(h.MatriculeSage, matriculeTo) <= 0);
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
        var cartes = (await _uow.Repository<CardPaie>().ListAsync(c => c.SageBdd == sageDb, ct))
            .Where(c => MatriculeRange.Contains(c.SageMatricule?.Trim(), periodRequest))
            .Where(c => string.IsNullOrWhiteSpace(filter.Departement)
                        || string.Equals(c.Departement?.Trim(), filter.Departement.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (cartes.Count == 0)
            return null;

        var idsalSet = cartes.Select(c => c.Id).ToHashSet();
        var clesCartes = cartes
            .Select(c => MatriculeIdentity.Cle(c.SageMatricule))
            .Where(c => !string.IsNullOrEmpty(c))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 2. Fériés de la base SAGE
        var feries = (await _uow.Repository<JourFerie>().ListAsync(f => f.BddSage == sageDb, ct))
            .Where(f => f.Date.HasValue)
            .Select(f => f.Date!.Value.Date)
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
        var corrections = (await _uow.Repository<HeureCorrigee>().ListAsync(h =>
            h.BddSage == sageDb
            && h.DatePointageIn >= from && h.DatePointageIn < borneFinExclusive, ct))
            .Where(h => clesCartes.Contains(MatriculeIdentity.Cle(h.MatriculeSage)))
            .ToList();

        // 5. Plannings de la plage [from, veille]. Les jours OFF sont conservés ici : ils sont
        // interprétés par les règles (un jour OFF n'est jamais une absence ni un retard).
        var plannings = (await _uow.Repository<PlanningSalarie>().ListAsync(p =>
            p.BddSage == sageDb
            && p.DateP >= from && p.DateP < borneFinExclusive, ct))
            .Where(p => idsalSet.Contains(p.IdSal))
            .ToList();

        // 6. Catégorie du salarié (T_CAT_SAL référence T_CARDPAIE.ID) et tolérances de catégorie
        var categorieParCarte = (await _uow.Repository<CategorieSalarie>().ListAsync(a => a.BddSage == sageDb, ct))
            .GroupBy(a => a.MatriculeSage)
            .ToDictionary(g => g.Key, g => g.First().IdCateg);
        var tolerances = await _uow.Repository<ToleranceRetard>().ListAsync(_ => true, ct);

        return new ReportContext(cartes, feries, conges, corrections, plannings, categorieParCarte, tolerances);
    }

    /// <summary>Données chargées une fois pour les états calculés (absences et retards).</summary>
    private sealed record ReportContext(
        IReadOnlyList<CardPaie> Cartes,
        IReadOnlySet<DateTime> Feries,
        IReadOnlyDictionary<string, List<LeaveDay>> Conges,
        IReadOnlyList<HeureCorrigee> Corrections,
        IReadOnlyList<PlanningSalarie> Plannings,
        IReadOnlyDictionary<int, int> CategorieParCarte,
        IReadOnlyList<ToleranceRetard> Tolerances);

    private static Expression<Func<HsExoImp, bool>> HsRecapPredicate(ReportFilter filter, string sageDb)
    {
        var from = filter.From.Date;
        var to = filter.To.Date;
        return h => h.BddSage == sageDb && h.PeriodeDebut == from && h.PeriodeFin == to;
    }

    private static Expression<Func<HsValidee, bool>> HeuresSemainePredicate(ReportFilter filter, string sageDb)
    {
        var from = filter.From.Date;
        var to = filter.To.Date;
        var matriculeFrom = filter.MatriculeFrom;
        var matriculeTo = filter.MatriculeTo;
        return h => h.BddSage == sageDb
            && h.DateDeb == from
            && h.DateFin == to
            && (string.IsNullOrEmpty(matriculeFrom) || string.Compare(h.Matricules, matriculeFrom) >= 0)
            && (string.IsNullOrEmpty(matriculeTo) || string.Compare(h.Matricules, matriculeTo) <= 0);
    }

    private static Expression<Func<HeureDimanche, bool>> HeuresDimanchePredicate(ReportFilter filter, string sageDb)
    {
        var from = filter.From;
        var to = filter.To;
        return h => h.BddSage == sageDb && h.DateDim >= from && h.DateDim <= to;
    }

    /// <summary>
    /// Recopie les retards calculés dans la table tampon d'impression <c>T_impr_RET</c>
    /// (équivalent du bouton « Générer état » du desktop). La table est purgée avant réinsertion.
    /// </summary>
    private async Task WriteRetardsPrintAsync(IReadOnlyList<RetardReportRow> rows, CancellationToken ct)
    {
        var print = _uow.Repository<ImpressionRetard>();
        var old = await print.ListAsync(_ => true, ct);
        print.RemoveRange(old);
        foreach (var row in rows)
        {
            await print.AddAsync(new ImpressionRetard
            {
                BddSage = _tenant.SageDb,
                BddPointeuse = _tenant.PointeuseDb,
                Matricules = row.Matricule,
                DatePointage = row.Date.ToString("dd/MM/yyyy"),
                HeureEntree = row.HeureEntree,
                Retards = TimeSpan.FromMinutes(row.MinutesRetard)
            }, ct);
        }
        await _uow.SaveChangesAsync(ct);
    }

    private static CorrectedHourDto ToCorrectedHourDto(HeureCorrigee a) => new(
        a.Id, a.MatriculeSage, a.DatePointageIn, a.DatePointageOut, a.HEntree, a.Hps, a.Hpe, a.HSortie,
        a.AbsenceAm, a.AbsencePm, a.IntituleAbsence, a.Retard, a.Ferier, a.FerierAm, a.FerierPm,
        a.ValiderCorrection, a.ValiderHs, a.Hs, a.MNuit, a.MDimanche, a.MFeries);

    private static HsExoDto ToHsExoDto(HsExoImp h) => new(
        h.Oid, h.Matricule, h.NumSal, h.Exo130, h.Exo150, h.I130, h.I150,
        h.Ferie, h.Nuit, h.Dim, h.Retard, h.Absence);

    private static object ToHeuresSemaineRow(HsValidee h)
    {
        var split = OvertimeRules.SplitWeekly(h.Hs ?? 0);
        return new
        {
            MATRICULES = h.Matricules,
            HT = h.Ht,
            HS = h.Hs,
            HS130 = split.Hs1,
            HS150 = split.Hs2,
            NUIT = h.Nuit,
            DIMANCHE = h.Dimanche,
            FERIES = h.Feries,
            RETARD = h.Retard
        };
    }

    private static object ToHeuresDimancheRow(HeureDimanche h) => new
    {
        MATRICULE_SAGE = h.MatriculeSage,
        Date_Dim = h.DateDim,
        h.HeureDebut,
        h.HeureFin,
        h.Valeur
    };
}
