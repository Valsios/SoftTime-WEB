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

    /// <summary>
    /// Droits affichés d'un compte : ses droits effectifs (rôle, plus ses ajouts, moins ses retraits), sans les anciens
    /// codes que la saisie refuserait. L'administrateur a toujours tous les droits par défaut, plus ses ajouts.
    /// IsCustomized : le compte a au moins un écart enregistré.
    /// </summary>
    public async Task<UserDetailedPermissionsDto> GetUserDetailedPermissionsAsync(decimal userId, CancellationToken cancellationToken = default)
    {
        var user = await _uow.Repository<Responsable>().GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("Utilisateur introuvable.");
        var deviations = await _uow.Repository<UserPermission>()
            .ListReadOnlyAsync(p => p.IdResponsable == userId, cancellationToken);
        var isCustomized = deviations.Count > 0;

        if (!AdminRules.IsSuperAdmin(user.IdRole))
        {
            var effective = await _effectiveRights.GetAsync(userId, fresh: true, ct: cancellationToken);
            var shown = effective.Permissions.Where(PermissionCodeValidator.IsValid).ToList();
            return new UserDetailedPermissionsDto(userId, shown, isCustomized);
        }

        // Administrateur : tous les droits par défaut, plus les ajouts du compte (ex. SELF:READ) et les lignes du rôle.
        var roleRows = await _uow.Repository<RolePermission>()
            .ListReadOnlyAsync(p => p.IdRole == user.IdRole && p.IsGranted, cancellationToken);
        var additions = deviations.Where(p => p.IsGranted).Select(p => p.PermissionCode)
            .Concat(roleRows.Select(p => p.PermissionCode))
            .Where(PermissionCodeValidator.IsValid);
        var defaults = await AdministratorDefaultsAsync(cancellationToken);
        return new UserDetailedPermissionsDto(userId, additions.Concat(defaults).Distinct().ToList(), isCustomized);
    }

    /// <summary>Tous les droits par défaut de l'administrateur : toutes les portes et toutes les bases.</summary>
    private async Task<List<string>> AdministratorDefaultsAsync(CancellationToken ct)
    {
        var allGates = new HashSet<int>
        {
            Rights.Databases, Rights.Parameters, Rights.Processing, Rights.Other, Rights.Traceability, Rights.Reports,
        };
        var sageDbs = await _uow.Repository<BddSage>().ListReadOnlyAsync(_ => true, ct);
        var pointeuseDbs = await _uow.Repository<BddPointeuse>().ListReadOnlyAsync(_ => true, ct);
        return PermissionCatalog.BuildDefaultPermissionsList(allGates, sageDbs, pointeuseDbs);
    }

    public async Task SaveUserDetailedPermissionsAsync(decimal currentUserId, decimal targetUserId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default)
    {
        var caller = await RequireAdministratorCallerAsync(currentUserId, cancellationToken);
        var target = await _uow.Repository<Responsable>().GetByIdAsync(targetUserId, cancellationToken)
            ?? throw new NotFoundException("Utilisateur cible introuvable.");

        var requested = PermissionCodeValidator.Normalize(permissions, PermissionMarkers.EmptyPermissionsMarker);
        var stored = await _uow.Repository<UserPermission>()
            .ListReadOnlyAsync(p => p.IdResponsable == targetUserId && p.IsGranted, cancellationToken);
        requested = PermissionCodeValidator.WithLegacyDatabaseCodes(requested, stored.Select(p => p.PermissionCode));

        var before = await _effectiveRights.GetAsync(targetUserId, fresh: true, ct: cancellationToken);
        var permsAvant = before.Permissions.ToList();
        var rows = await DeviationsAsync(target, requested, cancellationToken);
        await ReplaceUserPermissionsAsync(targetUserId, rows, cancellationToken);
        await _rightsChangeJournal.EcrireAuditDroitAsync(
            new RightsChange(currentUserId, caller.Login ?? string.Empty, "USER", targetUserId, target.NomResponsable, "UPDATE_USER_PERMS", permsAvant, requested),
            cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.Invalidate(targetUserId);
    }

    /// <summary>
    /// Écarts à enregistrer pour obtenir la liste demandée : ajouts (codes absents du rôle) et retraits (codes du
    /// rôle non demandés). Liste vide : marqueur « aucun droit ». Administrateur : le calcul ignore ses lignes,
    /// seuls ses ajouts (ex. SELF:READ) sont conservés.
    /// </summary>
    private async Task<List<UserPermission>> DeviationsAsync(
        Responsable target, List<string> requested, CancellationToken ct)
    {
        var id = target.IdResponsable;
        if (requested.Count == 0 && !AdminRules.IsSuperAdmin(target.IdRole))
        {
            return new List<UserPermission>
            {
                new() { IdResponsable = id, PermissionCode = PermissionMarkers.EmptyPermissionsMarker, IsGranted = true },
            };
        }

        var comparer = StringComparer.OrdinalIgnoreCase;
        var roleOnly = new HashSet<string>(
            await _effectiveRights.RoleDefaultPermissionsAsync(target.IdRole, ct), comparer);
        var wanted = new HashSet<string>(requested, comparer);
        var rows = requested.Where(c => !roleOnly.Contains(c))
            .Select(c => new UserPermission { IdResponsable = id, PermissionCode = c, IsGranted = true })
            .ToList();
        if (!AdminRules.IsSuperAdmin(target.IdRole))
            rows.AddRange(roleOnly.Where(c => !wanted.Contains(c)).OrderBy(c => c, comparer)
                .Select(c => new UserPermission { IdResponsable = id, PermissionCode = c, IsGranted = false }));
        return rows;
    }

    /// <summary>Remplace les écarts enregistrés du compte (au format écart, IS_DEVIATION = 1).</summary>
    private async Task ReplaceUserPermissionsAsync(decimal userId, List<UserPermission> rows, CancellationToken ct)
    {
        var repo = _uow.Repository<UserPermission>();
        repo.RemoveRange(await repo.ListAsync(p => p.IdResponsable == userId, ct));
        foreach (var row in rows)
            await repo.AddAsync(row, ct);
    }

    /// <summary>
    /// Retire des anciens codes de base sans action (« SAGE:X »), conservés jusque-là à chaque enregistrement pour ne
    /// retirer aucun accès sans le voir. Chaque code doit être un ancien code enregistré sur ce compte.
    /// </summary>
    public async Task RemoveLegacyPermissionsAsync(
        decimal currentUserId, decimal targetUserId, IReadOnlyCollection<string> codes, CancellationToken cancellationToken = default)
    {
        var caller = await RequireAdministratorCallerAsync(currentUserId, cancellationToken);
        var target = await _uow.Repository<Responsable>().GetByIdAsync(targetUserId, cancellationToken)
            ?? throw new NotFoundException("Utilisateur cible introuvable.");
        var repo = _uow.Repository<UserPermission>();
        var legacyRows = (await repo.ListAsync(p => p.IdResponsable == targetUserId, cancellationToken))
            .Where(p => PermissionCodeValidator.IsLegacyDatabaseCode(p.PermissionCode))
            .ToList();
        var wanted = (codes ?? Array.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToList();
        var missing = wanted.Where(c => !legacyRows.Any(r => string.Equals(r.PermissionCode.Trim(), c, StringComparison.OrdinalIgnoreCase))).ToList();
        if (wanted.Count == 0 || missing.Count > 0)
            throw new DomainException($"Ancien code introuvable pour ce compte : {string.Join(", ", missing.DefaultIfEmpty("(aucun)"))}.");

        var removed = legacyRows.Where(r => wanted.Contains(r.PermissionCode.Trim(), StringComparer.OrdinalIgnoreCase)).ToList();
        repo.RemoveRange(removed);
        await _rightsChangeJournal.EcrireAuditDroitAsync(
            new RightsChange(currentUserId, caller.Login ?? string.Empty, "USER", targetUserId, target.NomResponsable,
                "REMOVE_LEGACY_PERMS", removed.Select(r => r.PermissionCode).ToList(), new List<string>()),
            cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.Invalidate(targetUserId);
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
