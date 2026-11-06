using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services;

public sealed class AbsenceCodeService : IAbsenceCodeService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IExternalSourceService _externalSource;

    public AbsenceCodeService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IExternalSourceService externalSource)
    {
        _uow = uow;
        _tenant = tenant;
        _externalSource = externalSource;
    }

    public async Task<PagedResult<AbsenceCodeDto>> ListAbsenceCodesPagedAsync(
        int? page, int? pageSize, string? search = null, CancellationToken ct = default)
    {
        return await PagedLists.From(_uow.Repository<CodeAbsence>(), _ => true, q => q.OrderBy(a => a.IdAbsence)).WithSearch(search, new System.Linq.Expressions.Expression<Func<CodeAbsence, string?>>[] { a => a.IntituleAbsence }).PageAsync(new PageRequest(page, pageSize), a => new AbsenceCodeDto(a.IdAbsence, a.IntituleAbsence, a.NotPay), ct);
    }

    public async Task<IReadOnlyList<AbsenceCodeDto>> ListAbsenceCodesAsync(CancellationToken ct = default)
    {
        var list = await _uow.Repository<CodeAbsence>().ListReadOnlyAsync(_ => true, ct);
        return list.Select(a => new AbsenceCodeDto(a.IdAbsence, a.IntituleAbsence, a.NotPay)).ToList();
    }

    public async Task<AbsenceCodeDto> SaveAbsenceCodeAsync(AbsenceCodeDto dto, CancellationToken ct = default)
    {
        var repo = _uow.Repository<CodeAbsence>();
        CodeAbsence entity;
        if (dto.Id == 0)
        {
            entity = new CodeAbsence { IntituleAbsence = dto.Intitule, NotPay = dto.NotPay };
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new NotFoundException();
            entity.IntituleAbsence = dto.Intitule;
            entity.NotPay = dto.NotPay;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return new AbsenceCodeDto(entity.IdAbsence, entity.IntituleAbsence, entity.NotPay);
    }

    public async Task SyncAbsenceCodesFromSageAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageRow = await _tenant.GetSageRowAsync(ct);
        var events = await _externalSource.GetEventsAsync(sageRow, ct);
        var repo = _uow.Repository<CodeAbsence>();
        var existing = await repo.ListAsync(_ => true, ct);
        foreach (var ev in events)
        {
            if (existing.Any(e => e.IntituleAbsence == ev.Label || e.IntituleAbsence == ev.Code))
                continue;
            var newCode = new CodeAbsence { IntituleAbsence = ev.Label ?? ev.Code, NotPay = false };
            await repo.AddAsync(newCode, ct);
            existing.Add(newCode);
        }
        await _uow.SaveChangesAsync(ct);
    }
}
