using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Permissions;

public sealed class PrivilegeService : IPrivilegeService
{
    private readonly IUnitOfWork _uow;
    private readonly IEffectiveRightsService _effectiveRights;
    private readonly IParametersAccessGuard _parametersAccessGuard;

    public PrivilegeService(
        IUnitOfWork uow,
        IEffectiveRightsService effectiveRights,
        IParametersAccessGuard parametersAccessGuard)
    {
        _uow = uow;
        _effectiveRights = effectiveRights;
        _parametersAccessGuard = parametersAccessGuard;
    }

    public async Task<IReadOnlyList<DroitDto>> GetDroitsAsync(CancellationToken cancellationToken = default)
    {
        await _parametersAccessGuard.EnsureCanReadAsync(ReadPermissions.PrivilegeList, cancellationToken);
        return (await _uow.Repository<DroitAcces>().ListReadOnlyAsync(_ => true, cancellationToken))
            .Select(d => new DroitDto(d.IdDroit, d.Droit)).ToList();
    }

    public async Task<IReadOnlyList<PrivilegeDto>> GetPrivilegesAsync(decimal? roleId, CancellationToken cancellationToken = default)
    {
        await _parametersAccessGuard.EnsureCanReadAsync(ReadPermissions.PrivilegeList, cancellationToken);
        var list = await _uow.Repository<DroitRole>().ListReadOnlyAsync(
            p => !roleId.HasValue || p.IdRole == roleId.Value, cancellationToken);
        return list.Select(p => new PrivilegeDto(p.IdDroitRole, p.IdRole, p.IdDroit)).ToList();
    }

    public async Task SetPrivilegesAsync(decimal roleId, IReadOnlyCollection<decimal> droitIds, CancellationToken cancellationToken = default)
    {
        if (roleId == AdminRules.AdminRoleId)
            throw new DomainException("Les privilèges du rôle Administrateur ne sont pas modifiables.");
        // Les portes d'un rôle ayant des droits détaillés se déduisent de ces droits (SaveRoleDetailedPermissionsAsync) : écrire
        // T_DROIT_ROLE à la main n'aurait aucun effet sur le jeton et le contredirait.
        if ((await _uow.Repository<RolePermission>().ListAsync(p => p.IdRole == roleId, cancellationToken)).Count > 0)
            throw new DomainException("Ce rôle a des droits détaillés : modifiez-les depuis la page Privilèges, les portes en sont déduites.");
        var repo = _uow.Repository<DroitRole>();
        var existing = await repo.ListAsync(p => p.IdRole == roleId, cancellationToken);
        repo.RemoveRange(existing);
        foreach (var d in droitIds.Distinct())
            await repo.AddAsync(new DroitRole { IdRole = roleId, IdDroit = d }, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.InvalidateAll();
    }
}
