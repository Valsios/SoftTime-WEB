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
    private readonly IRightsChangeJournal _rightsChangeJournal;

    public RoleService(
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

    /// <summary>
    /// Crée ou renomme un rôle (administrateur seulement) et le journalise. À la création, l'identifiant n'est connu
    /// qu'après l'enregistrement : la ligne du journal porte 0 et le nom du rôle.
    /// </summary>
    public async Task<RoleDto> SaveRoleAsync(RoleDto dto, CancellationToken cancellationToken = default)
    {
        _parametersAccessGuard.RequireAdministrator();
        if (dto.Id == AdminRules.AdminRoleId)
            throw new DomainException("Le rôle Administrateur ne peut pas être modifié ni renommé.");
        var repo = _uow.Repository<Role>();
        Role entity;
        RightsChangeEntry entry;
        if (dto.Id == 0)
        {
            entity = new Role { NomRole = dto.Nom };
            await repo.AddAsync(entity, cancellationToken);
            entry = new RightsChangeEntry(RightsChangeActions.RoleTarget, 0, dto.Nom, RightsChangeActions.CreateRole, null, null);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, cancellationToken) ?? throw new NotFoundException();
            entry = new RightsChangeEntry(
                RightsChangeActions.RoleTarget, dto.Id, dto.Nom, RightsChangeActions.RenameRole, NameList(entity.NomRole), NameList(dto.Nom));
            entity.NomRole = dto.Nom;
            repo.Update(entity);
        }
        await _rightsChangeJournal.RecordAsync(entry, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.InvalidateAll();
        return new RoleDto(entity.IdRole, entity.NomRole);
    }

    /// <summary>
    /// Supprime un rôle (administrateur seulement) avec ses droits. Refusé (409) tant qu'un compte y est rattaché :
    /// ce compte perdrait tous ses droits sans que personne ne l'ait décidé.
    /// </summary>
    public async Task DeleteRoleAsync(decimal id, CancellationToken cancellationToken = default)
    {
        _parametersAccessGuard.RequireAdministrator();
        if (id == AdminRules.AdminRoleId)
            throw new DomainException("Le rôle Administrateur ne peut pas être supprimé.");
        var repo = _uow.Repository<Role>();
        var entity = await repo.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException();

        var holders = (await _uow.Repository<Responsable>().ListReadOnlyAsync(u => u.IdRole == id, cancellationToken)).Count;
        if (holders > 0)
            throw new ConflictException(
                $"Ce rôle est attribué à {holders} utilisateur(s) : attribuez-leur un autre rôle avant de le supprimer.");

        var rpRepo = _uow.Repository<RolePermission>();
        var rolePerms = await rpRepo.ListAsync(p => p.IdRole == id, cancellationToken);
        rpRepo.RemoveRange(rolePerms);

        var droitRepo = _uow.Repository<DroitRole>();
        droitRepo.RemoveRange(await droitRepo.ListAsync(d => d.IdRole == id, cancellationToken));

        repo.Remove(entity);
        await _rightsChangeJournal.RecordAsync(
            new RightsChangeEntry(
                RightsChangeActions.RoleTarget, id, entity.NomRole, RightsChangeActions.DeleteRole,
                rolePerms.Where(p => p.PermissionCode != PermissionCatalog.EmptyRoleMarker).Select(p => p.PermissionCode).ToList(), null),
            cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.InvalidateAll();
    }

    private static IEnumerable<string>? NameList(string? name) => name is null ? null : new[] { name };
}
