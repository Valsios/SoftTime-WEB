using SoftTime.Application.Abstractions.Settings;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services.Settings;

public sealed class ClockParameterService : IClockParameterService
{
    private readonly IUnitOfWork _uow;

    public ClockParameterService(
        IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<ClockParamDto> GetClockAsync(CancellationToken ct = default)
    {
        var row = (await _uow.Repository<ParametrePointeuse>().ListReadOnlyAsync(_ => true, ct)).FirstOrDefault();
        return row == null ? new ClockParamDto(0, false) : new ClockParamDto(row.IdPoint, row.MultiPoint);
    }

    public async Task<ClockParamDto> SaveClockAsync(ClockParamDto dto, CancellationToken ct = default)
    {
        var repo = _uow.Repository<ParametrePointeuse>();
        var row = (await repo.ListAsync(_ => true, ct)).FirstOrDefault();
        if (row == null)
        {
            row = new ParametrePointeuse { MultiPoint = dto.MultiPoint };
            await repo.AddAsync(row, ct);
        }
        else
        {
            row.MultiPoint = dto.MultiPoint;
            repo.Update(row);
        }
        await _uow.SaveChangesAsync(ct);
        return new ClockParamDto(row.IdPoint, row.MultiPoint);
    }
}
