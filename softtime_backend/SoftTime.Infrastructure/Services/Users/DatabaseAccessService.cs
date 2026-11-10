using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Abstractions.Users;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services.Users;

public sealed class DatabaseAccessService : IDatabaseAccessService
{
    private readonly IUnitOfWork _uow;
    private readonly IEffectiveRightsService _effectiveRights;
    private readonly IParametersAccessGuard _parametersAccessGuard;
    private readonly ICurrentOperatorRightsJournal _journal;

    public DatabaseAccessService(
        IUnitOfWork uow,
        IEffectiveRightsService effectiveRights,
        IParametersAccessGuard parametersAccessGuard,
        ICurrentOperatorRightsJournal journal)
    {
        _uow = uow;
        _effectiveRights = effectiveRights;
        _parametersAccessGuard = parametersAccessGuard;
        _journal = journal;
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
        var user = await _uow.Repository<Responsable>().GetByIdAsync(userId, cancellationToken);
        await _journal.RecordAsync(new RightsTarget("USER", userId, user?.NomResponsable), "UPDATE_USER_DB_ACCESS",
            await NamesAsync(existing.Select(a => a.IdBdd), cancellationToken),
            await NamesAsync(sageDbIds.Distinct(), cancellationToken), cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.Invalidate(userId);
    }

    /// <summary>Bases cochées, par nom (identifiant si la base n'existe plus).</summary>
    private async Task<List<string>> NamesAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var wanted = ids.ToHashSet();
        var names = (await _uow.Repository<BddSage>().ListReadOnlyAsync(s => wanted.Contains(s.Id), ct))
            .ToDictionary(s => s.Id, s => s.NomBd);
        return wanted.OrderBy(i => i).Select(i => $"BASE:{(names.TryGetValue(i, out var n) && n is not null ? n : i.ToString())}").ToList();
    }
}
