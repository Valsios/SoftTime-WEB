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
    private readonly IRightsChangeJournal _rightsChangeJournal;

    public DatabaseAccessService(
        IUnitOfWork uow,
        IEffectiveRightsService effectiveRights,
        IParametersAccessGuard parametersAccessGuard,
        IRightsChangeJournal rightsChangeJournal)
    {
        _uow = uow;
        _effectiveRights = effectiveRights;
        _parametersAccessGuard = parametersAccessGuard;
        _rightsChangeJournal = rightsChangeJournal;
    }

    public async Task<IReadOnlyList<DbAccessDto>> GetDbAccessAsync(decimal? userId, CancellationToken cancellationToken = default)
    {
        await _parametersAccessGuard.EnsureCanReadAsync(ReadPermissions.DbAccess, cancellationToken);
        var list = await _uow.Repository<BddAutorisee>().ListReadOnlyAsync(
            a => !userId.HasValue || a.IdResponsable == userId.Value, cancellationToken);
        return list.Select(a => new DbAccessDto(a.IdBddAutorise, a.IdResponsable, a.IdBdd)).ToList();
    }

    /// <summary>Remplace les bases SAGE cochées pour un compte (administrateur seulement), bases inconnues refusées, et journalise.</summary>
    public async Task SetDbAccessAsync(decimal userId, IReadOnlyCollection<int> sageDbIds, CancellationToken cancellationToken = default)
    {
        _parametersAccessGuard.RequireAdministrator();
        var user = await _uow.Repository<Responsable>().GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("Utilisateur introuvable.");
        var bases = (await _uow.Repository<BddSage>().ListReadOnlyAsync(_ => true, cancellationToken))
            .ToDictionary(b => b.Id, b => b.NomBd ?? string.Empty);
        var requested = (sageDbIds ?? Array.Empty<int>()).Distinct().ToList();
        var unknown = requested.Where(id => !bases.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
            throw new DomainException($"Bases inconnues : {string.Join(", ", unknown)}.");

        var repo = _uow.Repository<BddAutorisee>();
        var existing = await repo.ListAsync(a => a.IdResponsable == userId, cancellationToken);
        repo.RemoveRange(existing);
        foreach (var id in requested)
            await repo.AddAsync(new BddAutorisee { IdResponsable = userId, IdBdd = id }, cancellationToken);
        await _rightsChangeJournal.RecordAsync(
            new RightsChangeEntry(
                RightsChangeActions.UserTarget, userId, user.NomResponsable, RightsChangeActions.UpdateDatabaseAccess,
                Names(existing.Select(a => a.IdBdd), bases), Names(requested, bases)),
            cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.Invalidate(userId);
    }

    private static List<string> Names(IEnumerable<int> ids, IReadOnlyDictionary<int, string> bases)
        => ids.Select(id => bases.TryGetValue(id, out var name) ? name : $"#{id}").OrderBy(n => n, StringComparer.Ordinal).ToList();
}
