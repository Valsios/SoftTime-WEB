using AutoMapper;

using SoftTime.Application.Abstractions.ExternalData;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Abstractions.Settings;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Settings;

public sealed class HolidayService : IHolidayService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly ITenantConnectionService _tenant;
    private readonly IExternalSourceService _externalSource;

    public HolidayService(
        IUnitOfWork uow,
        IMapper mapper,
        ITenantConnectionService tenant,
        IExternalSourceService externalSource)
    {
        _uow = uow;
        _mapper = mapper;
        _tenant = tenant;
        _externalSource = externalSource;
    }

    public async Task<PagedResult<HolidayDto>> ListHolidaysPagedAsync(
        int? page, int? pageSize, string? search = null, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var list = await _uow.Repository<JourFerie>().ListReadOnlyAsync(f => f.BddSage == sage, ct);
        var rows = list
            .OrderBy(f => f.Id)
            .Select(_mapper.Map<HolidayDto>)
            .Where(f => SearchText.Matches(search, f.Intitule, f.Date))
            .ToList();
        return PagedLists.Page(rows, page, pageSize);
    }

    public async Task<IReadOnlyList<HolidayDto>> ListHolidaysAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var list = await _uow.Repository<JourFerie>().ListReadOnlyAsync(f => f.BddSage == sage, ct);
        return list.Select(_mapper.Map<HolidayDto>).ToList();
    }

    public async Task<HolidayDto> SaveHolidayAsync(HolidayDto dto, CancellationToken ct = default)
    {
        var repo = _uow.Repository<JourFerie>();
        JourFerie entity;
        if (dto.Id == 0)
        {
            var duplicate = dto.Date.HasValue
                ? (await repo.ListAsync(f => f.Date.HasValue && f.Date.Value.Date == dto.Date.Value.Date, ct)).FirstOrDefault()
                : null;
            if (duplicate != null)
            {
                duplicate.Intitule = dto.Intitule;
                repo.Update(duplicate);
                entity = duplicate;
            }
            else
            {
                entity = new JourFerie { Intitule = dto.Intitule, Date = dto.Date };
                await repo.AddAsync(entity, ct);
            }
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new NotFoundException();
            entity.Intitule = dto.Intitule;
            entity.Date = dto.Date;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return _mapper.Map<HolidayDto>(entity);
    }

    public async Task DeleteHolidayAsync(int id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<JourFerie>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new NotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task ImportSageHolidaysAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageRow = await _tenant.GetSageRowAsync(ct);
        var holidays = await _externalSource.GetCompanyCalendarAsync(sageRow, ct);
        var repo = _uow.Repository<JourFerie>();
        var existing = await repo.ListAsync(f => f.BddSage == _tenant.SageDb, ct);
        foreach (var day in holidays)
        {
            var date = day.Date;
            if (existing.Any(e => e.Date.HasValue && e.Date.Value.Date == date))
                continue;
            await repo.AddAsync(new JourFerie { Date = date, Intitule = "Férié SAGE", BddSage = _tenant.SageDb }, ct);
        }
        await _uow.SaveChangesAsync(ct);
    }
}
