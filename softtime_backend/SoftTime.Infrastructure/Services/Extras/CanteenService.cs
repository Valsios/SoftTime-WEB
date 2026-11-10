using SoftTime.Application.Abstractions.Extras;
using SoftTime.Application.Abstractions.Punches;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Infrastructure.Queries;

namespace SoftTime.Infrastructure.Services.Extras;

public sealed class CanteenService : ICanteenService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IPunchAggregate _punches;
    private readonly IPayrollCumulSync _payrollCumulSync;

    public CanteenService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IPunchAggregate punches,
        IPayrollCumulSync payrollCumulSync)
    {
        _uow = uow;
        _tenant = tenant;
        _punches = punches;
        _payrollCumulSync = payrollCumulSync;
    }

    /// <summary>Constante SAGE portant le cumul cantine (même code que le desktop).</summary>
    public const string CodeConstanteCantine = "CANTINE_CODE";

    public async Task<IReadOnlyList<CanteenDto>> ListCanteenAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var from = request.From.Date;
        var to = request.To.Date;
        var matriculeFrom = request.MatriculeFrom;
        var matriculeTo = request.MatriculeTo;
        var list = await _uow.Repository<Cantine>().ListReadOnlyAsync(
            c => c.NomBddSage == sageDb
                 && c.DateDeb >= from
                 && c.DateFin <= to
                 && (string.IsNullOrEmpty(matriculeFrom)
                     || (c.MatriculeSage != null && string.Compare(c.MatriculeSage, matriculeFrom) >= 0))
                 && (string.IsNullOrEmpty(matriculeTo)
                     || (c.MatriculeSage != null && string.Compare(c.MatriculeSage, matriculeTo) <= 0)), ct);
        return list
            .OrderBy(c => c.MatriculeSage)
            .Select(c => new CanteenDto(c.IdCantine, c.MatriculeSage, c.DateDeb, c.DateFin, c.TotalCantine))
            .ToList();
    }

    /// <summary>Liste paginée des cantines calculées (filtre SQL base + dates + matricule).</summary>
    public async Task<PagedResult<CanteenDto>> ListCanteenPagedAsync(
        PeriodRequest request, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var from = request.From.Date;
        var to = request.To.Date;
        var matriculeFrom = request.MatriculeFrom;
        var matriculeTo = request.MatriculeTo;
        return await PagedLists.From(_uow.Repository<Cantine>(), c => c.NomBddSage == sageDb
                 && c.DateDeb >= from
                 && c.DateFin <= to
                 && (string.IsNullOrEmpty(matriculeFrom)
                     || (c.MatriculeSage != null && string.Compare(c.MatriculeSage, matriculeFrom) >= 0))
                 && (string.IsNullOrEmpty(matriculeTo)
                     || (c.MatriculeSage != null && string.Compare(c.MatriculeSage, matriculeTo) <= 0)), q => q.OrderBy(c => c.MatriculeSage).ThenBy(c => c.DateDeb).ThenBy(c => c.IdCantine)).WithSearch(request.Search, new System.Linq.Expressions.Expression<Func<Cantine, string?>>[] { c => c.MatriculeSage }).PageAsync(new PageRequest(page, pageSize), c => new CanteenDto(c.IdCantine, c.MatriculeSage, c.DateDeb, c.DateFin, c.TotalCantine), ct);
    }

    public async Task<IReadOnlyList<CanteenDto>> ComputeCanteenAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var from = request.From.Date;
        var to = request.To.Date;
        var jours = await _punches.ListCanteenDaysAsync(_tenant.SageDb, from, to.AddDays(1), ct);
        var counts = jours
            .GroupBy(j => j.Matricule)
            .Select(g => new PunchCountDto(g.Key, g.Select(j => j.Date.Date).Distinct().Count()))
            .ToList();
        var repo = _uow.Repository<Cantine>();
        var old = await repo.ListAsync(
            c => c.NomBddSage == _tenant.SageDb && c.DateDeb == from && c.DateFin == to, ct);
        repo.RemoveRange(old);
        var result = new List<CanteenDto>();
        foreach (var row in counts)
        {
            var entity = new Cantine
            {
                NomBddSage = _tenant.SageDb,
                NomBddPointeuse = _tenant.PointeuseDb,
                MatriculeSage = row.Matricule,
                DateDeb = from,
                DateFin = to,
                TotalCantine = row.Total
            };
            await repo.AddAsync(entity, ct);
            result.Add(new CanteenDto(entity.IdCantine, entity.MatriculeSage, entity.DateDeb, entity.DateFin, entity.TotalCantine));
        }
        await _uow.SaveChangesAsync(ct);
        return result.OrderBy(r => r.Matricule).ToList();
    }

    /// <summary>
    /// Anomalies du calcul cantine : jour où un pointage tombe dans la fenêtre du repas alors que
    /// les heures corrigées déclarent une absence complète (matin et après-midi).
    /// Le desktop remontait une anomalie pour <b>toute</b> absence complète ; ici seules les
    /// contradictions entre les deux sources sont signalées, sinon chaque journée d'absence
    /// produirait une ligne inutile. Les anomalies sont dérivées, jamais stockées.
    /// </summary>
    public async Task<IReadOnlyList<CanteenAnomalyDto>> ListCanteenAnomaliesAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var from = request.From.Date;
        var to = request.To.Date;
        var sageDb = _tenant.SageDb;

        var joursPointes = (await _punches.ListCanteenDaysAsync(sageDb, from, to.AddDays(1), ct))
            .Where(j => MatriculeRange.Contains(j.Matricule, request))
            .ToList();

        var corrections = await _uow.Repository<HeureCorrigee>().ListReadOnlyAsync(h =>
            h.BddSage == sageDb
            && h.DatePointageIn != null
            && h.DatePointageIn >= from
            && h.DatePointageIn < to.AddDays(1)
            // Filtre métier poussé en SQL : seules les absences complètes peuvent contredire un pointage.
            && h.AbsenceAm == true
            && h.AbsencePm == true
            && (string.IsNullOrEmpty(request.MatriculeFrom)
                || (h.MatriculeSage != null && string.Compare(h.MatriculeSage, request.MatriculeFrom) >= 0))
            && (string.IsNullOrEmpty(request.MatriculeTo)
                || (h.MatriculeSage != null && string.Compare(h.MatriculeSage, request.MatriculeTo) <= 0)), ct);
        var absences = corrections
            .Where(h => h.DatePointageIn.HasValue && !string.IsNullOrEmpty(h.MatriculeSage))
            .Select(h => new CanteenDay(h.MatriculeSage!, h.DatePointageIn!.Value.Date))
            .Where(d => MatriculeRange.Contains(d.Matricule, request))
            .ToList();

        return CanteenRules.DetecterAnomalies(joursPointes, absences)
            .Select(a => new CanteenAnomalyDto(a.Matricule, a.Date, a.Motif))
            .ToList();
    }

    /// <summary>
    /// Anomalies cantine paginées : le calcul complet est fait côté serveur, puis la page est
    /// découpée <b>après</b> agrégation (jamais un Skip/Take sur les sources).
    /// </summary>
    public async Task<PagedResult<CanteenAnomalyDto>> ListCanteenAnomaliesPagedAsync(
        PeriodRequest request, int? page, int? pageSize, CancellationToken ct = default)
    {
        var rows = await ListCanteenAnomaliesAsync(request, ct);
        return PagedLists.Page(
            rows.Where(row => SearchText.Matches(request.Search, row.Matricule, row.Motif)).ToList(),
            page,
            pageSize);
    }

    /// <summary>
    /// Purge les lignes cantine de la période (portage de <c>PurgerCantines</c> du desktop, qui
    /// filtrait sur <c>DateDebut &gt;= from</c>, <c>DateFin &lt;= to</c> et la plage de matricules).
    /// Une période vide est refusée : elle viderait toute la table.
    /// </summary>
    public async Task<PurgeExtrasResultDto> PurgeCanteenAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        ExtraPeriods.EnsurePeriode(request);
        var repo = _uow.Repository<Cantine>();
        var from = request.From.Date;
        var to = request.To.Date;
        var lignes = (await repo.ListAsync(c =>
                c.NomBddSage == _tenant.SageDb && c.DateDeb >= from && c.DateFin <= to, ct))
            .Where(c => MatriculeRange.Contains(c.MatriculeSage, request))
            .ToList();
        if (lignes.Count == 0)
            return new PurgeExtrasResultDto(0, "Aucune cantine à purger sur la période.");
        repo.RemoveRange(lignes);
        await _uow.SaveChangesAsync(ct);
        return new PurgeExtrasResultDto(lignes.Count, $"{lignes.Count} ligne(s) cantine supprimée(s).");
    }

    /// <summary>
    /// Pousse les totaux cantine de la période dans le cumul SAGE (<c>T_CUMSAL</c>) référencé par la
    /// constante <c>CANTINE_CODE</c>. Portage de <c>SynchroniserCantines</c> du desktop.
    /// <para>
    /// Écarts assumés : (1) les valeurs poussées sont les lignes <b>stockées</b> de la période, et non
    /// le corps de la requête comme le desktop (qui faisait confiance au client) ; (2) la valeur poussée
    /// est le <b>total calculé</b> (<c>TOTAL_CANTINE</c>) alors que le desktop écrivait la constante
    /// <c>'1'</c> marquée dans son code source comme provisoire — à confirmer avec le client ;
    /// (3) la constante SAGE absente provoque un refus, au lieu du cumul arbitraire 123 du desktop.
    /// </para>
    /// </summary>
    public Task<ExtrasSyncResultDto> SyncCanteenAsync(PeriodRequest request, CancellationToken ct = default)
        => _payrollCumulSync.SyncExtrasAsync(request, CodeConstanteCantine, cantine: true, ct);
}
