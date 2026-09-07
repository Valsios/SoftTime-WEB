using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Application.Services;

public class ExtraService
{
    private readonly IUnitOfWork _uow;
    private readonly TenantConnectionService _tenant;
    private readonly IPunchAggregate _punches;

    public ExtraService(IUnitOfWork uow, TenantConnectionService tenant, IPunchAggregate punches)
    {
        _uow = uow;
        _tenant = tenant;
        _punches = punches;
    }

    public async Task<IReadOnlyList<CanteenDto>> ListCanteenAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var from = request.From.Date;
        var to = request.To.Date;
        var list = await _uow.Repository<T_CANTINE>().ListAsync(c => c.NOM_BDD_SAGE == _tenant.SageDb, ct);
        return list
            .Where(c => c.DATEDEB?.Date >= from && c.DATEFIN?.Date <= to
                        && InMatriculeRange(c.MATRICULE_SAGE, request.MatriculeFrom, request.MatriculeTo))
            .OrderBy(c => c.MATRICULE_SAGE)
            .Select(c => new CanteenDto(c.IDCANTINE, c.MATRICULE_SAGE, c.DATEDEB, c.DATEFIN, c.TOTAL_CANTINE))
            .ToList();
    }

    public async Task<IReadOnlyList<TravelDto>> ListTravelAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var from = request.From.Date;
        var to = request.To.Date;
        var list = await _uow.Repository<T_FRAIS>().ListAsync(c => c.NOM_BDD_SAGE == _tenant.SageDb, ct);
        return list
            .Where(c => c.DATEDEB?.Date >= from && c.DATEFIN?.Date <= to
                        && InMatriculeRange(c.MATRICULE_SAGE, request.MatriculeFrom, request.MatriculeTo))
            .OrderBy(c => c.MATRICULE_SAGE)
            .Select(c => new TravelDto(c.IDPOINTAGE, c.MATRICULE_SAGE, c.DATEDEB, c.DATEFIN, c.TOTAL_POINT))
            .ToList();
    }

    public async Task<IReadOnlyList<CanteenDto>> ComputeCanteenAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var from = request.From.Date;
        var to = request.To.Date;
        var counts = await _punches.CountCanteenDaysAsync(_tenant.SageDb, from, to.AddDays(1), ct);
        var repo = _uow.Repository<T_CANTINE>();
        var old = await repo.ListAsync(
            c => c.NOM_BDD_SAGE == _tenant.SageDb && c.DATEDEB == from && c.DATEFIN == to, ct);
        repo.RemoveRange(old);
        var result = new List<CanteenDto>();
        foreach (var row in counts)
        {
            var entity = new T_CANTINE
            {
                NOM_BDD_SAGE = _tenant.SageDb,
                NOM_BDD_POINTEUSE = _tenant.PointeuseDb,
                MATRICULE_SAGE = row.Matricule,
                DATEDEB = from,
                DATEFIN = to,
                TOTAL_CANTINE = row.Total
            };
            await repo.AddAsync(entity, ct);
            result.Add(new CanteenDto(entity.IDCANTINE, entity.MATRICULE_SAGE, entity.DATEDEB, entity.DATEFIN, entity.TOTAL_CANTINE));
        }
        await _uow.SaveChangesAsync(ct);
        return result.OrderBy(r => r.Matricule).ToList();
    }

    public async Task<IReadOnlyList<TravelDto>> ComputeTravelAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var from = request.From.Date;
        var to = request.To.Date;
        var counts = await _punches.CountTravelPunchesAsync(_tenant.SageDb, from, to.AddDays(1), ct);
        var repo = _uow.Repository<T_FRAIS>();
        var old = await repo.ListAsync(
            c => c.NOM_BDD_SAGE == _tenant.SageDb && c.DATEDEB == from && c.DATEFIN == to, ct);
        repo.RemoveRange(old);
        var result = new List<TravelDto>();
        foreach (var row in counts)
        {
            var entity = new T_FRAIS
            {
                NOM_BDD_SAGE = _tenant.SageDb,
                NOM_BDD_POINTEUSE = _tenant.PointeuseDb,
                MATRICULE_SAGE = row.Matricule,
                DATEDEB = from,
                DATEFIN = to,
                TOTAL_POINT = row.Total
            };
            await repo.AddAsync(entity, ct);
            result.Add(new TravelDto(entity.IDPOINTAGE, entity.MATRICULE_SAGE, entity.DATEDEB, entity.DATEFIN, entity.TOTAL_POINT));
        }
        await _uow.SaveChangesAsync(ct);
        return result.OrderBy(r => r.Matricule).ToList();
    }

    private static bool InMatriculeRange(string? matricule, string? from, string? to)
    {
        if (string.IsNullOrEmpty(matricule))
            return false;
        if (!string.IsNullOrEmpty(from) && string.Compare(matricule, from, StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        if (!string.IsNullOrEmpty(to) && string.Compare(matricule, to, StringComparison.OrdinalIgnoreCase) > 0)
            return false;
        return true;
    }
}
