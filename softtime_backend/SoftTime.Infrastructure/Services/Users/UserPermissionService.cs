using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Abstractions.Users;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Infrastructure.Services.Permissions;

namespace SoftTime.Infrastructure.Services.Users;

public sealed class UserPermissionService : IUserPermissionService
{
    private readonly IUnitOfWork _uow;
    private readonly IEffectiveRightsService _effectiveRights;
    private readonly IParametersAccessGuard _parametersAccessGuard;
    private readonly IRightsChangeJournal _rightsChangeJournal;

    public UserPermissionService(
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

    private static List<string> BuildDefaultPermissionsList(
        HashSet<int> rights,
        IEnumerable<BddSage> sageDbs,
        IEnumerable<BddPointeuse> pointeuseDbs)
        => PermissionCatalog.BuildDefaultPermissionsList(rights, sageDbs, pointeuseDbs);

    public async Task<UserDetailedPermissionsDto> GetUserDetailedPermissionsAsync(decimal userId, CancellationToken cancellationToken = default)
    {
        var user = await _uow.Repository<Responsable>().GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("Utilisateur introuvable.");
        var isAdmin = user.IdRole == 1;
        var customList = await _uow.Repository<UserPermission>().ListReadOnlyAsync(
            p => p.IdResponsable == userId && p.IsGranted, cancellationToken);
        var defaults = await DefaultPermissionsAsync(user, cancellationToken);

        // Un administrateur doit impérativement avoir tous les privilèges système
        // même si une personnalisation partielle a été enregistrée.
        if (customList.Count > 0)
        {
            var perms = customList
                .Where(p => p.PermissionCode != PermissionMarkers.EmptyPermissionsMarker)
                .Select(p => p.PermissionCode)
                .Where(PermissionCodeValidator.IsValid);
            return new UserDetailedPermissionsDto(userId, WithAdminDefaults(perms, isAdmin, defaults), true);
        }

        // B2 — Droits effectifs : droits du rôle si configurés
        var roleCustomPerms = await _uow.Repository<RolePermission>().ListReadOnlyAsync(
            p => p.IdRole == user.IdRole && p.IsGranted, cancellationToken);
        if (roleCustomPerms.Count > 0)
        {
            var rolePerms = roleCustomPerms
                .Where(p => p.PermissionCode != PermissionMarkers.EmptyRolePermissionsMarker)
                .Select(p => p.PermissionCode)
                .Where(PermissionCodeValidator.IsValid);
            return new UserDetailedPermissionsDto(userId, WithAdminDefaults(rolePerms, isAdmin, defaults), false);
        }

        // Rôle sans lignes et compte sans ajustement : les droits affichés sont les droits effectifs du calculateur
        // (sans la configuration des bases). L'administrateur garde ses droits par défaut complets.
        if (!isAdmin)
            return new UserDetailedPermissionsDto(userId, (await _effectiveRights.GetAsync(userId, fresh: true, ct: cancellationToken)).Permissions.ToList(), false);
        return new UserDetailedPermissionsDto(userId, defaults.Distinct().ToList(), false);
    }

    /// <summary>
    /// Permissions par défaut déduites des droits du rôle (tous pour l'administrateur) et des bases
    /// visibles : toutes pour l'administrateur ou le droit Bases, sinon celles de T_BDD_AUTORISE.
    /// </summary>
    private async Task<List<string>> DefaultPermissionsAsync(Responsable user, CancellationToken ct)
    {
        var isAdmin = user.IdRole == 1;
        var rolePrivileges = await _uow.Repository<DroitRole>().ListReadOnlyAsync(p => p.IdRole == user.IdRole, ct);
        var rights = isAdmin
            ? new HashSet<int> { Rights.Databases, Rights.Parameters, Rights.Processing, Rights.Other, Rights.Traceability, Rights.Reports }
            : rolePrivileges.Select(p => (int)p.IdDroit).ToHashSet();

        var access = await _uow.Repository<BddAutorisee>().ListReadOnlyAsync(a => a.IdResponsable == user.IdResponsable, ct);
        var sageIds = access.Select(a => a.IdBdd).ToHashSet();
        var sageDbs = isAdmin || rights.Contains(Rights.Databases)
            ? await _uow.Repository<BddSage>().ListReadOnlyAsync(_ => true, ct)
            : await _uow.Repository<BddSage>().ListReadOnlyAsync(s => sageIds.Contains(s.Id), ct);
        var pointeuseDbs = await _uow.Repository<BddPointeuse>().ListReadOnlyAsync(_ => true, ct);
        return BuildDefaultPermissionsList(rights, sageDbs, pointeuseDbs);
    }

    private static List<string> WithAdminDefaults(IEnumerable<string> permissions, bool isAdmin, List<string> defaults) =>
        (isAdmin ? permissions.Concat(defaults) : permissions).Distinct().ToList();

    public async Task SaveUserDetailedPermissionsAsync(decimal currentUserId, decimal targetUserId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default)
    {
        var caller = await RequireAdministratorCallerAsync(currentUserId, cancellationToken);
        var target = await _uow.Repository<Responsable>().GetByIdAsync(targetUserId, cancellationToken)
            ?? throw new NotFoundException("Utilisateur cible introuvable.");

        var toSaveList = PermissionCodeValidator.Normalize(permissions, PermissionMarkers.EmptyPermissionsMarker);
        if (target.IdRole == AdminRules.AdminRoleId)
            toSaveList = PermissionCodeValidator.Normalize(
                toSaveList.Concat(await AdminMandatoryPermissionsAsync(cancellationToken)), PermissionMarkers.EmptyPermissionsMarker);

        var permsAvant = await ReplaceUserPermissionsAsync(targetUserId, toSaveList, cancellationToken);
        await _rightsChangeJournal.EcrireAuditDroitAsync(
            new RightsChange(currentUserId, caller.Login ?? string.Empty, "USER", targetUserId, target.NomResponsable, "UPDATE_USER_PERMS", permsAvant, toSaveList),
            cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.Invalidate(targetUserId);
    }

    /// <summary>
    /// Un administrateur doit obligatoirement conserver l'accès à toutes les bases et aux écrans clés.
    /// </summary>
    private async Task<List<string>> AdminMandatoryPermissionsAsync(CancellationToken ct)
    {
        var codes = new List<string>
        {
            "SAGE:CREATE", "POINTEUSE:CREATE", "PARAMS:PRIVILEGES", "PARAMS:ROLES", "PARAMS:USERS", "PARAMS:DB_ACCESS"
        };
        var sages = await _uow.Repository<BddSage>().ListAsync(_ => true, ct);
        codes.AddRange(PermissionCatalog.DatabasePermissions("SAGE", sages.Select(s => s.NomBd)));
        var pointeuses = await _uow.Repository<BddPointeuse>().ListAsync(_ => true, ct);
        codes.AddRange(PermissionCatalog.DatabasePermissions("POINTEUSE", pointeuses.Select(p => p.NomBd)));
        return codes;
    }

    /// <summary>
    /// Remplace les permissions de l'utilisateur. Une ligne sentinelle conserve l'intention « aucun
    /// privilège » : sans elle, l'absence de ligne serait interprétée comme un retour aux droits du
    /// rôle. Retourne les permissions d'avant pour le journal.
    /// </summary>
    private async Task<List<string>> ReplaceUserPermissionsAsync(
        decimal userId, List<string> permissions, CancellationToken ct)
    {
        var repo = _uow.Repository<UserPermission>();
        var existing = await repo.ListAsync(p => p.IdResponsable == userId, ct);
        var permsAvant = existing
            .Where(p => p.PermissionCode != PermissionMarkers.EmptyPermissionsMarker)
            .Select(p => p.PermissionCode)
            .ToList();
        repo.RemoveRange(existing);

        var codes = permissions.Count == 0
            ? new List<string> { PermissionMarkers.EmptyPermissionsMarker }
            : permissions;
        foreach (var code in codes)
            await repo.AddAsync(new UserPermission { IdResponsable = userId, PermissionCode = code, IsGranted = true }, ct);
        return permsAvant;
    }

    public async Task ResetUserDetailedPermissionsAsync(decimal currentUserId, decimal targetUserId, CancellationToken cancellationToken = default)
    {
        var caller = await RequireAdministratorCallerAsync(currentUserId, cancellationToken);
        var target = await _uow.Repository<Responsable>().GetByIdAsync(targetUserId, cancellationToken)
            ?? throw new NotFoundException("Utilisateur cible introuvable.");

        var permsAvant = await SupprimerPermissionsAsync(targetUserId, cancellationToken);

        await _rightsChangeJournal.EcrireAuditDroitAsync(
            new RightsChange(currentUserId, caller.Login ?? string.Empty, "USER", targetUserId, target.NomResponsable, "RESET_USER_PERMS", permsAvant, null),
            cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.Invalidate(targetUserId);
    }

    /// <summary>
    /// L'appelant doit être administrateur dans le jeton ET encore administrateur en base : un compte rétrogradé
    /// dont le jeton n'a pas encore expiré est refusé, quel que soit le compte visé (lui-même ou un autre).
    /// </summary>
    private async Task<Responsable> RequireAdministratorCallerAsync(decimal currentUserId, CancellationToken ct)
    {
        _parametersAccessGuard.RequireAdministrator();
        var caller = await _uow.Repository<Responsable>().GetByIdAsync(currentUserId, ct)
            ?? throw new UnauthorizedException("Utilisateur connecté introuvable.");
        if (!AdminRules.IsSuperAdmin(caller.IdRole))
            throw new ForbiddenException(AdminAccess.ReservedMessage);
        return caller;
    }

    /// <summary>Supprime les ajustements de l'utilisateur et retourne les permissions d'avant pour le journal.</summary>
    private async Task<List<string>> SupprimerPermissionsAsync(
        decimal targetUserId, CancellationToken cancellationToken)
    {
        var repo = _uow.Repository<UserPermission>();
        var existing = await repo.ListAsync(p => p.IdResponsable == targetUserId, cancellationToken);
        var permsAvant = existing
            .Where(p => p.PermissionCode != PermissionMarkers.EmptyPermissionsMarker)
            .Select(p => p.PermissionCode)
            .ToList();

        repo.RemoveRange(existing);
        return permsAvant;
    }
}
