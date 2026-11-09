using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Permissions;

public sealed class RoleService : IRoleService
{
    private readonly IUnitOfWork _uow;
    private readonly IEffectiveRightsService _effectiveRights;
    private readonly IParametersAccessGuard _parametersAccessGuard;

    public RoleService(
        IUnitOfWork uow,
        IEffectiveRightsService effectiveRights,
        IParametersAccessGuard parametersAccessGuard)
    {
        _uow = uow;
        _effectiveRights = effectiveRights;
        _parametersAccessGuard = parametersAccessGuard;
    }

    public async Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        await _parametersAccessGuard.EnsureCanReadAsync(ReadPermissions.RoleList, cancellationToken);
        return (await _uow.Repository<Role>().ListReadOnlyAsync(_ => true, cancellationToken))
            .Select(r => new RoleDto(r.IdRole, r.NomRole)).ToList();
    }

    public async Task<PagedResult<RoleDto>> GetRolesPagedAsync(
        int? page, int? pageSize, string? search = null, CancellationToken cancellationToken = default)
    {
        await _parametersAccessGuard.EnsureCanReadAsync(ReadPermissions.Roles, cancellationToken);
        return await PagedLists.From(_uow.Repository<Role>(), _ => true, q => q.OrderBy(r => r.IdRole)).WithSearch(search, new System.Linq.Expressions.Expression<Func<Role, string?>>[] { r => r.NomRole }).PageAsync(new PageRequest(page, pageSize), r => new RoleDto(r.IdRole, r.NomRole), cancellationToken);
    }

    public async Task<RoleDto> SaveRoleAsync(RoleDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Id == AdminRules.AdminRoleId)
            throw new DomainException("Le rôle Administrateur ne peut pas être modifié ni renommé.");
        var repo = _uow.Repository<Role>();
        Role entity;
        if (dto.Id == 0)
        {
            entity = new Role { NomRole = dto.Nom };
            await repo.AddAsync(entity, cancellationToken);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, cancellationToken) ?? throw new NotFoundException();
            entity.NomRole = dto.Nom;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.InvalidateAll();
        return new RoleDto(entity.IdRole, entity.NomRole);
    }

    public async Task DeleteRoleAsync(decimal id, CancellationToken cancellationToken = default)
    {
        if (id == AdminRules.AdminRoleId)
            throw new DomainException("Le rôle Administrateur ne peut pas être supprimé.");
        var repo = _uow.Repository<Role>();
        var entity = await repo.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException();

        // C5 — Nettoyage à la suppression d'un rôle
        var rpRepo = _uow.Repository<RolePermission>();
        var rolePerms = await rpRepo.ListAsync(p => p.IdRole == id, cancellationToken);
        rpRepo.RemoveRange(rolePerms);

        var droitRepo = _uow.Repository<DroitRole>();
        var droits = await droitRepo.ListAsync(d => d.IdRole == id, cancellationToken);
        droitRepo.RemoveRange(droits);

        repo.Remove(entity);
        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.InvalidateAll();
    }
}
