using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Domain;

namespace SoftTime.Infrastructure.Services;

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

        var customList = await _uow.Repository<UserPermission>().ListReadOnlyAsync(
            p => p.IdResponsable == userId && p.IsGranted, cancellationToken);

        var rolePrivileges = await _uow.Repository<DroitRole>().ListReadOnlyAsync(p => p.IdRole == user.IdRole, cancellationToken);
        var rights = rolePrivileges.Select(p => (int)p.IdDroit).ToHashSet();
        var isAdmin = user.IdRole == 1;
        if (isAdmin)
        {
            rights = new HashSet<int> { Rights.Databases, Rights.Parameters, Rights.Processing, Rights.Other, Rights.Traceability, Rights.Reports };
        }

        var access = await _uow.Repository<BddAutorisee>().ListReadOnlyAsync(a => a.IdResponsable == userId, cancellationToken);
        var sageIds = access.Select(a => a.IdBdd).ToHashSet();
        // Admin : toutes les bases ; autres : seulement celles de T_BDD_AUTORISE.
        var sageDbs = (isAdmin || rights.Contains(Rights.Databases))
            ? await _uow.Repository<BddSage>().ListReadOnlyAsync(_ => true, cancellationToken)
            : await _uow.Repository<BddSage>().ListReadOnlyAsync(s => sageIds.Contains(s.Id), cancellationToken);
        var pointeuseDbs = await _uow.Repository<BddPointeuse>().ListReadOnlyAsync(_ => true, cancellationToken);

        var defaults = BuildDefaultPermissionsList(rights, sageDbs, pointeuseDbs);

        if (customList.Count > 0)
        {
            var perms = customList
                .Where(p => p.PermissionCode != PermissionMarkers.EmptyPermissionsMarker)
                .Select(p => p.PermissionCode)
                .ToList();

            if (isAdmin)
            {
                // Un administrateur doit impérativement avoir tous les privilèges système
                // même si une personnalisation partielle a été enregistrée.
                perms.AddRange(defaults);
            }
            return new UserDetailedPermissionsDto(userId, perms.Distinct().ToList(), true);
        }

        // B2 — Droits effectifs : droits du rôle si configurés
        var roleCustomPerms = await _uow.Repository<RolePermission>().ListReadOnlyAsync(
            p => p.IdRole == user.IdRole && p.IsGranted, cancellationToken);
        if (roleCustomPerms.Count > 0)
        {
            var rolePerms = roleCustomPerms
                .Where(p => p.PermissionCode != PermissionMarkers.EmptyRolePermissionsMarker)
                .Select(p => p.PermissionCode)
                .Distinct()
                .ToList();
            if (isAdmin)
            {
                rolePerms.AddRange(defaults);
                rolePerms = rolePerms.Distinct().ToList();
            }
            return new UserDetailedPermissionsDto(userId, rolePerms, false);
        }

        // Rôle sans lignes et compte sans ajustement : les droits affichés sont les droits effectifs du calculateur
        // (sans la configuration des bases). L'administrateur garde ses droits par défaut complets.
        if (!isAdmin)
            return new UserDetailedPermissionsDto(userId, (await _effectiveRights.GetAsync(userId, fresh: true, ct: cancellationToken)).Permissions.ToList(), false);
        return new UserDetailedPermissionsDto(userId, defaults.Distinct().ToList(), false);
    }

    public async Task SaveUserDetailedPermissionsAsync(decimal currentUserId, decimal targetUserId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default)
    {
        _parametersAccessGuard.RequireAdministrator();
        var caller = await _uow.Repository<Responsable>().GetByIdAsync(currentUserId, cancellationToken)
            ?? throw new UnauthorizedException("Utilisateur connecté introuvable.");

        var target = await _uow.Repository<Responsable>().GetByIdAsync(targetUserId, cancellationToken)
            ?? throw new NotFoundException("Utilisateur cible introuvable.");

        var callerIsAdmin = AdminRules.IsSuperAdmin(caller.IdRole);

        if (currentUserId == targetUserId && !callerIsAdmin)
        {
            throw new DomainException("Un utilisateur ne peut pas modifier ses propres privilèges sans droit d'administration.");
        }

        var targetIsAdmin = target.IdRole == 1;

        var toSaveList = permissions
            .Where(p => !string.IsNullOrWhiteSpace(p) && p != PermissionMarkers.EmptyPermissionsMarker)
            .ToList();

        if (targetIsAdmin)
        {
            // Un administrateur doit obligatoirement conserver l'accès à toutes les bases et aux écrans clés
            toSaveList.Add("SAGE:CREATE");
            toSaveList.Add("POINTEUSE:CREATE");
            toSaveList.Add("PARAMS:PRIVILEGES");
            toSaveList.Add("PARAMS:ROLES");
            toSaveList.Add("PARAMS:USERS");
            toSaveList.Add("PARAMS:DB_ACCESS");

            var allSages = await _uow.Repository<BddSage>().ListAsync(_ => true, cancellationToken);
            foreach (var s in allSages)
            {
                var n = s.NomBd ?? string.Empty;
                if (string.IsNullOrWhiteSpace(n)) continue;
                toSaveList.Add($"SAGE:{n}:READ");
                toSaveList.Add($"SAGE:{n}:UPDATE");
                toSaveList.Add($"SAGE:{n}:DELETE");
            }

            var allPointeuses = await _uow.Repository<BddPointeuse>().ListAsync(_ => true, cancellationToken);
            foreach (var p in allPointeuses)
            {
                var n = p.NomBd ?? string.Empty;
                if (string.IsNullOrWhiteSpace(n)) continue;
                toSaveList.Add($"POINTEUSE:{n}:READ");
                toSaveList.Add($"POINTEUSE:{n}:UPDATE");
                toSaveList.Add($"POINTEUSE:{n}:DELETE");
            }
        }

        var repo = _uow.Repository<UserPermission>();
        var existing = await repo.ListAsync(p => p.IdResponsable == targetUserId, cancellationToken);
        var permsAvant = existing
            .Where(p => p.PermissionCode != PermissionMarkers.EmptyPermissionsMarker)
            .Select(p => p.PermissionCode)
            .ToList();
        repo.RemoveRange(existing);

        foreach (var p in toSaveList.Distinct())
        {
            await repo.AddAsync(new UserPermission
            {
                IdResponsable = targetUserId,
                PermissionCode = p.Trim(),
                IsGranted = true
            }, cancellationToken);
        }

        // Une ligne sentinelle conserve l'intention "aucun privilège" : sans elle,
        // l'absence de ligne serait interprétée comme un retour aux droits du rôle.
        if (toSaveList.Count == 0)
        {
            await repo.AddAsync(new UserPermission
            {
                IdResponsable = targetUserId,
                PermissionCode = PermissionMarkers.EmptyPermissionsMarker,
                IsGranted = true
            }, cancellationToken);
        }

        await _rightsChangeJournal.EcrireAuditDroitAsync(
            currentUserId, caller.Login ?? string.Empty,
            "USER", targetUserId, target.NomResponsable,
            "UPDATE_USER_PERMS",
            permsAvant, toSaveList,
            cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.Invalidate(targetUserId);
    }

    public async Task ResetUserDetailedPermissionsAsync(decimal currentUserId, decimal targetUserId, CancellationToken cancellationToken = default)
    {
        _parametersAccessGuard.RequireAdministrator();
        var caller = await _uow.Repository<Responsable>().GetByIdAsync(currentUserId, cancellationToken)
            ?? throw new UnauthorizedException("Utilisateur connecté introuvable.");

        var target = await _uow.Repository<Responsable>().GetByIdAsync(targetUserId, cancellationToken)
            ?? throw new NotFoundException("Utilisateur cible introuvable.");

        var callerIsAdmin = AdminRules.IsSuperAdmin(caller.IdRole);

        if (currentUserId == targetUserId && !callerIsAdmin)
        {
            throw new DomainException("Un utilisateur ne peut pas modifier ses propres privilèges sans droit d'administration.");
        }

        var repo = _uow.Repository<UserPermission>();
        var existing = await repo.ListAsync(p => p.IdResponsable == targetUserId, cancellationToken);
        var permsAvant = existing
            .Where(p => p.PermissionCode != PermissionMarkers.EmptyPermissionsMarker)
            .Select(p => p.PermissionCode)
            .ToList();

        repo.RemoveRange(existing);

        await _rightsChangeJournal.EcrireAuditDroitAsync(
            currentUserId, caller.Login ?? string.Empty,
            "USER", targetUserId, target.NomResponsable,
            "RESET_USER_PERMS",
            permsAvant, null,
            cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.Invalidate(targetUserId);
    }
}
