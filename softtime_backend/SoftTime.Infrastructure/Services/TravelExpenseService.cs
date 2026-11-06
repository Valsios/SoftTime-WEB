using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services;

public sealed class TravelExpenseService : ITravelExpenseService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IPunchAggregate _punches;
    private readonly IPayrollCumulSync _payrollCumulSync;

    public TravelExpenseService(
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

    /// <summary>Constante SAGE portant le cumul des frais de déplacement (même code que le desktop).</summary>
    public const string CodeConstanteFrais = "HA06";

    public async Task<IReadOnlyList<TravelDto>> ListTravelAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var from = request.From.Date;
        var to = request.To.Date;
        var matriculeFrom = request.MatriculeFrom;
        var matriculeTo = request.MatriculeTo;
        var list = await _uow.Repository<Frais>().ListReadOnlyAsync(
            c => c.NomBddSage == sageDb
                 && c.DateDeb >= from
                 && c.DateFin <= to
                 && (string.IsNullOrEmpty(matriculeFrom)
                     || (c.MatriculeSage != null && string.Compare(c.MatriculeSage, matriculeFrom) >= 0))
                 && (string.IsNullOrEmpty(matriculeTo)
                     || (c.MatriculeSage != null && string.Compare(c.MatriculeSage, matriculeTo) <= 0)), ct);
        return list
            .OrderBy(c => c.MatriculeSage)
            .Select(c => new TravelDto(c.IdPointage, c.MatriculeSage, c.DateDeb, c.DateFin, c.TotalPoint))
            .ToList();
    }

    /// <summary>Liste paginée des frais de déplacement (filtre SQL base + dates + matricule).</summary>
    public async Task<PagedResult<TravelDto>> ListTravelPagedAsync(
        PeriodRequest request, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var from = request.From.Date;
        var to = request.To.Date;
        var matriculeFrom = request.MatriculeFrom;
        var matriculeTo = request.MatriculeTo;
        return await PagedLists.From(_uow.Repository<Frais>(), c => c.NomBddSage == sageDb
                 && c.DateDeb >= from
                 && c.DateFin <= to
                 && (string.IsNullOrEmpty(matriculeFrom)
                     || (c.MatriculeSage != null && string.Compare(c.MatriculeSage, matriculeFrom) >= 0))
                 && (string.IsNullOrEmpty(matriculeTo)
                     || (c.MatriculeSage != null && string.Compare(c.MatriculeSage, matriculeTo) <= 0)), q => q.OrderBy(c => c.MatriculeSage).ThenBy(c => c.DateDeb).ThenBy(c => c.IdPointage)).WithSearch(request.Search, new System.Linq.Expressions.Expression<Func<Frais, string?>>[] { c => c.MatriculeSage }).PageAsync(new PageRequest(page, pageSize), c => new TravelDto(c.IdPointage, c.MatriculeSage, c.DateDeb, c.DateFin, c.TotalPoint), ct);
    }

    public async Task<IReadOnlyList<TravelDto>> ComputeTravelAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var from = request.From.Date;
        var to = request.To.Date;
        var counts = await _punches.CountTravelPunchesAsync(_tenant.SageDb, from, to.AddDays(1), ct);
        var repo = _uow.Repository<Frais>();
        var old = await repo.ListAsync(
            c => c.NomBddSage == _tenant.SageDb && c.DateDeb == from && c.DateFin == to, ct);
        repo.RemoveRange(old);
        var result = new List<TravelDto>();
        foreach (var row in counts)
        {
            var entity = new Frais
            {
                NomBddSage = _tenant.SageDb,
                NomBddPointeuse = _tenant.PointeuseDb,
                MatriculeSage = row.Matricule,
                DateDeb = from,
                DateFin = to,
                TotalPoint = row.Total
            };
            await repo.AddAsync(entity, ct);
            result.Add(new TravelDto(entity.IdPointage, entity.MatriculeSage, entity.DateDeb, entity.DateFin, entity.TotalPoint));
        }
        await _uow.SaveChangesAsync(ct);
        return result.OrderBy(r => r.Matricule).ToList();
    }

    /// <summary>
    /// Pousse les totaux de frais de déplacement de la période dans le cumul SAGE (<c>T_CUMSAL</c>)
    /// référencé par la constante <c>HA06</c>. Portage de <c>SynchroniserAvecSageAsync</c> du desktop.
    /// </summary>
    public Task<ExtrasSyncResultDto> SyncTravelAsync(PeriodRequest request, CancellationToken ct = default)
        => _payrollCumulSync.SyncExtrasAsync(request, CodeConstanteFrais, cantine: false, ct);
}
