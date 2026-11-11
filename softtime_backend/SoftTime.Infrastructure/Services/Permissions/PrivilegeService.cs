using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services.Permissions;

/// <summary>
/// Catalogue des permissions et lecture des portes des rôles. Les portes (T_DROIT_ROLE) ne s'écrivent plus à la main :
/// elles se déduisent des droits détaillés du rôle (RolePermissionService).
/// </summary>
public sealed class PrivilegeService : IPrivilegeService
{
    private readonly IUnitOfWork _uow;
    private readonly IParametersAccessGuard _parametersAccessGuard;

    public PrivilegeService(IUnitOfWork uow, IParametersAccessGuard parametersAccessGuard)
    {
        _uow = uow;
        _parametersAccessGuard = parametersAccessGuard;
    }

    public IReadOnlyList<PermissionCategoryDto> GetCatalog()
        => PermissionCatalog.Categories
            .Select(c => new PermissionCategoryDto(
                c.Key,
                c.Title,
                c.Gate,
                c.Items.Select(i => new PermissionItemDto(i.Code, i.Label, i.CanCreate, i.CanUpdate, i.CanDelete)).ToList()))
            .ToList();

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
}
