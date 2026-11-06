using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services;

public sealed class DatabaseAccessService : IDatabaseAccessService
{
    private readonly IUnitOfWork _uow;
    private readonly IEffectiveRightsService _effectiveRights;
    private readonly IParametersAccessGuard _parametersAccessGuard;

    public DatabaseAccessService(
        IUnitOfWork uow,
        IEffectiveRightsService effectiveRights,
        IParametersAccessGuard parametersAccessGuard)
    {
        _uow = uow;
        _effectiveRights = effectiveRights;
        _parametersAccessGuard = parametersAccessGuard;
    }

    public async Task<IReadOnlyList<DbAccessDto>> GetDbAccessAsync(decimal? userId, CancellationToken cancellationToken = default)
    {
        await _parametersAccessGuard.EnsureCanReadAsync(ReadPermissions.DbAccess, cancellationToken);
        var list = await _uow.Repository<BddAutorisee>().ListReadOnlyAsync(
            a => !userId.HasValue || a.IdResponsable == userId.Value, cancellationToken);
        return list.Select(a => new DbAccessDto(a.IdBddAutorise, a.IdResponsable, a.IdBdd)).ToList();
    }

    public async Task SetDbAccessAsync(decimal userId, IReadOnlyCollection<int> sageDbIds, CancellationToken cancellationToken = default)
    {
        _parametersAccessGuard.RequireAdministrator();
        var repo = _uow.Repository<BddAutorisee>();
        var existing = await repo.ListAsync(a => a.IdResponsable == userId, cancellationToken);
        repo.RemoveRange(existing);
        foreach (var id in sageDbIds.Distinct())
            await repo.AddAsync(new BddAutorisee { IdResponsable = userId, IdBdd = id }, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.Invalidate(userId);
    }
}
