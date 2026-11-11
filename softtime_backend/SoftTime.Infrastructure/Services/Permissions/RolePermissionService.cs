using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Permissions;

public sealed class RolePermissionService : IRolePermissionService
{
    /// <summary>Le rôle Administrateur n'a pas de ligne : il a tous les droits, sur toutes les bases présentes et futures.</summary>
    private static readonly HashSet<int> s_allGates = new() { 1, 2, 3, 4, 5, 6 };

    private readonly IUnitOfWork _uow;
    private readonly IEffectiveRightsService _effectiveRights;
    private readonly IParametersAccessGuard _parametersAccessGuard;
    private readonly IRightsChangeJournal _rightsChangeJournal;

    public RolePermissionService(
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

    public async Task<RoleDetailedPermissionsDto> GetRoleDetailedPermissionsAsync(decimal roleId, CancellationToken cancellationToken = default)
    {
        await _parametersAccessGuard.EnsureCanReadAsync(ReadPermissions.Privileges, cancellationToken);
        if (roleId == AdminRules.AdminRoleId)
            return new RoleDetailedPermissionsDto(roleId, PermissionCatalog.BuildDefaultPermissionsList(s_allGates), true);
        var roleList = await _uow.Repository<RolePermission>().ListReadOnlyAsync(
            p => p.IdRole == roleId && p.IsGranted, cancellationToken);

        // Un rôle sans ligne n'accorde aucun droit (les portes ne donnent plus de droits par défaut).
        if (roleList.Count == 0)
            return new RoleDetailedPermissionsDto(roleId, Array.Empty<string>(), false);

        var perms = roleList
            .Where(p => p.PermissionCode != PermissionCatalog.EmptyRoleMarker)
            .Select(p => p.PermissionCode)
            .Distinct()
            .ToList();

        return new RoleDetailedPermissionsDto(roleId, perms, true);
    }

    public async Task SaveRoleDetailedPermissionsAsync(decimal currentUserId, decimal targetRoleId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default)
    {
        var normalizedPermissions = PermissionCodeNormalizer.Normalize(permissions);
        var caller = await RequireAdministratorCallerAsync(currentUserId, cancellationToken);
        if (targetRoleId == AdminRules.AdminRoleId)
            throw new DomainException("Les privilèges du rôle Administrateur ne sont pas modifiables.");
        var targetRole = await _uow.Repository<Role>().GetByIdAsync(targetRoleId, cancellationToken)
            ?? throw new NotFoundException("Rôle cible introuvable.");

        var permsAvant = await ReplaceRolePermissionsAsync(targetRoleId, normalizedPermissions, cancellationToken);
        await ReplaceDroitsAsync(targetRoleId, normalizedPermissions, cancellationToken);

        await _rightsChangeJournal.EcrireAuditDroitAsync(
            new RightsChange(currentUserId, caller.Login ?? string.Empty, RightsChangeActions.RoleTarget, targetRoleId, targetRole.NomRole, "UPDATE_ROLE_PERMS", permsAvant, normalizedPermissions),
            cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.InvalidateAll();
    }

    /// <summary>Administrateur exigé (403) : d'après le jeton, puis d'après le compte en base.</summary>
    private async Task<Responsable> RequireAdministratorCallerAsync(decimal currentUserId, CancellationToken ct)
    {
        _parametersAccessGuard.RequireAdministrator();
        var caller = await _uow.Repository<Responsable>().GetByIdAsync(currentUserId, ct)
            ?? throw new UnauthorizedException("Utilisateur connecté introuvable.");
        if (!AdminRules.IsSuperAdmin(caller.IdRole))
            throw new ForbiddenException("Seul un administrateur peut modifier les permissions d'un rôle.");
        return caller;
    }

    /// <summary>
    /// Remplace les permissions du rôle ; une liste vide est mémorisée par un marqueur. Retourne les
    /// permissions d'avant pour le journal.
    /// </summary>
    private async Task<List<string>> ReplaceRolePermissionsAsync(
        decimal roleId, List<string> permissions, CancellationToken ct)
    {
        var rpRepo = _uow.Repository<RolePermission>();
        var existing = await rpRepo.ListAsync(p => p.IdRole == roleId, ct);
        var permsAvant = existing
            .Where(p => p.PermissionCode != PermissionCatalog.EmptyRoleMarker)
            .Select(p => p.PermissionCode)
            .ToList();
        rpRepo.RemoveRange(existing);

        var codes = permissions.Count == 0
            ? new List<string> { PermissionCatalog.EmptyRoleMarker }
            : permissions;
        foreach (var code in codes)
            await rpRepo.AddAsync(new RolePermission { IdRole = roleId, PermissionCode = code, IsGranted = true }, ct);
        return permsAvant;
    }

    /// <summary>Recalcule les portes d'écran (T_DROITROLE) déduites des permissions.</summary>
    private async Task ReplaceDroitsAsync(decimal roleId, List<string> permissions, CancellationToken ct)
    {
        var droitRepo = _uow.Repository<DroitRole>();
        droitRepo.RemoveRange(await droitRepo.ListAsync(d => d.IdRole == roleId, ct));
        foreach (var porte in PermissionCatalog.DeduirePortes(permissions))
            await droitRepo.AddAsync(new DroitRole { IdRole = roleId, IdDroit = porte }, ct);
    }
}
