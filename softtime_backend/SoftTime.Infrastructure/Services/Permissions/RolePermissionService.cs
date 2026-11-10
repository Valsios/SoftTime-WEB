using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Permissions;

public sealed class RolePermissionService : IRolePermissionService
{
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

    private static readonly HashSet<string> KnownStaticPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "PARAMS:USERS", "PARAMS:USERS:CREATE", "PARAMS:USERS:UPDATE", "PARAMS:USERS:DELETE",
        "PARAMS:ROLES", "PARAMS:ROLES:CREATE", "PARAMS:ROLES:UPDATE", "PARAMS:ROLES:DELETE",
        "PARAMS:PRIVILEGES", "PARAMS:DB_ACCESS", "PARAMS:CLOCK_PARAMS", "PARAMS:CORRESPONDENCE",
        "PARAMS:CARDPAIE", "PARAMS:CARDPAIE:CREATE", "PARAMS:CARDPAIE:UPDATE", "PARAMS:CARDPAIE:DELETE",
        "PARAMS:CATEGORIES", "PARAMS:CATEGORIES:CREATE", "PARAMS:CATEGORIES:UPDATE", "PARAMS:CATEGORIES:DELETE",
        "PARAMS:AFFECTATIONS", "PARAMS:AFFECTATIONS:CREATE", "PARAMS:AFFECTATIONS:UPDATE", "PARAMS:AFFECTATIONS:DELETE",
        "PARAMS:TOLERANCES", "PARAMS:TOLERANCES:CREATE", "PARAMS:TOLERANCES:UPDATE",
        "PARAMS:HOLIDAYS", "PARAMS:HOLIDAYS:CREATE", "PARAMS:HOLIDAYS:UPDATE", "PARAMS:HOLIDAYS:DELETE",
        "PARAMS:MAJORATIONS", "PARAMS:MAJORATIONS:UPDATE",
        "PARAMS:CODE_CONSTANTES",
        "PARAMS:ABSENCE_CODES", "PARAMS:ABSENCE_CODES:CREATE", "PARAMS:ABSENCE_CODES:UPDATE",
        "PROC:SHIFTS", "PROC:SHIFTS:CREATE", "PROC:SHIFTS:UPDATE", "PROC:SHIFTS:DELETE",
        "PROC:EMPLOYEE_PLANNING", "PROC:EMPLOYEE_PLANNING:CREATE", "PROC:EMPLOYEE_PLANNING:UPDATE", "PROC:EMPLOYEE_PLANNING:DELETE",
        "PROC:PUNCHES",
        "PROC:ANOMALIES", "PROC:ANOMALIES:DELETE",
        "PROC:CORRECTED_HOURS", "PROC:CORRECTED_HOURS:CREATE", "PROC:CORRECTED_HOURS:UPDATE", "PROC:CORRECTED_HOURS:DELETE",
        "PROC:WEEKLY_VALIDATION", "PROC:OVERTIME",
        "OTHER:CANTEEN", "OTHER:TRAVEL_EXPENSES",
        "TRACE:AUDIT",
        "REPORTS:REPORTS", "REPORTS:PREDICTIONS", "REPORTS:CLOSING"
    };

    private static bool IsKnownPermissionCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        if (code.StartsWith("SAGE:", StringComparison.OrdinalIgnoreCase) ||
            code.StartsWith("POINTEUSE:", StringComparison.OrdinalIgnoreCase))
            return true;
        return KnownStaticPermissions.Contains(code);
    }

    private static HashSet<decimal> DeduirePortes(IEnumerable<string> permCodes)
        => PermissionCatalog.DeduirePortes(permCodes);

    public async Task<RoleDetailedPermissionsDto> GetRoleDetailedPermissionsAsync(decimal roleId, CancellationToken cancellationToken = default)
    {
        await _parametersAccessGuard.EnsureCanReadAsync(ReadPermissions.Privileges, cancellationToken);
        var roleList = await _uow.Repository<RolePermission>().ListReadOnlyAsync(
            p => p.IdRole == roleId && p.IsGranted, cancellationToken);

        if (roleList.Count == 0)
        {
            var defaultPerms = (await _effectiveRights.RoleDefaultPermissionsAsync(roleId, cancellationToken)).ToList();
            return new RoleDetailedPermissionsDto(roleId, defaultPerms, false);
        }

        var perms = roleList
            .Where(p => p.PermissionCode != PermissionMarkers.EmptyRolePermissionsMarker)
            .Select(p => p.PermissionCode)
            .Distinct()
            .ToList();

        return new RoleDetailedPermissionsDto(roleId, perms, true);
    }

    public async Task SaveRoleDetailedPermissionsAsync(decimal currentUserId, decimal targetRoleId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default)
    {
        if (permissions == null)
            throw new DomainException("La liste des permissions ne peut pas être nulle.");

        var caller = await RequireAdministratorCallerAsync(currentUserId, cancellationToken);
        if (targetRoleId == AdminRules.AdminRoleId)
            throw new DomainException("Les privilèges du rôle Administrateur ne sont pas modifiables.");
        var targetRole = await _uow.Repository<Role>().GetByIdAsync(targetRoleId, cancellationToken)
            ?? throw new NotFoundException("Rôle cible introuvable.");

        var normalizedPermissions = NormalizePermissions(permissions);
        var permsAvant = await ReplaceRolePermissionsAsync(targetRoleId, normalizedPermissions, cancellationToken);
        await ReplaceDroitsAsync(targetRoleId, normalizedPermissions, cancellationToken);

        await _rightsChangeJournal.EcrireAuditDroitAsync(
            new RightsChange(currentUserId, caller.Login ?? string.Empty, "ROLE", targetRoleId, targetRole.NomRole, "UPDATE_ROLE_PERMS", permsAvant, normalizedPermissions),
            cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.InvalidateAll();
    }

    private async Task<Responsable> RequireAdministratorCallerAsync(decimal currentUserId, CancellationToken ct)
    {
        var caller = await _uow.Repository<Responsable>().GetByIdAsync(currentUserId, ct)
            ?? throw new DomainException("Utilisateur appelant introuvable.");
        _ = await _uow.Repository<Role>().GetByIdAsync(caller.IdRole, ct)
            ?? throw new DomainException("Utilisateur appelant introuvable.");
        if (!AdminRules.IsSuperAdmin(caller.IdRole))
            throw new DomainException("Seul un administrateur peut modifier les permissions d'un rôle.");
        return caller;
    }

    /// <summary>
    /// Codes nettoyés et dédoublonnés : majuscules sauf les codes de base (SAGE:/POINTEUSE:, dont le
    /// nom de base est conservé), marqueur « aucune permission » retiré, codes trop longs ou
    /// inconnus refusés.
    /// </summary>
    private static List<string> NormalizePermissions(IReadOnlyCollection<string> permissions)
    {
        var normalized = permissions
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Select(p => p.StartsWith("SAGE:", StringComparison.OrdinalIgnoreCase)
                         || p.StartsWith("POINTEUSE:", StringComparison.OrdinalIgnoreCase)
                ? p
                : p.ToUpperInvariant())
            .Where(p => p != PermissionMarkers.EmptyRolePermissionsMarker)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var tooLongCodes = normalized.Where(p => p.Length > 128).ToList();
        if (tooLongCodes.Count > 0)
            throw new DomainException($"Code de permission trop long (128 caractères maximum) : {string.Join(", ", tooLongCodes)}.");

        var invalidCodes = normalized.Where(p => !IsKnownPermissionCode(p)).ToList();
        if (invalidCodes.Count > 0)
            throw new DomainException($"Codes de permission inconnus : {string.Join(", ", invalidCodes)}.");
        return normalized;
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
            .Where(p => p.PermissionCode != PermissionMarkers.EmptyRolePermissionsMarker)
            .Select(p => p.PermissionCode)
            .ToList();
        rpRepo.RemoveRange(existing);

        var codes = permissions.Count == 0
            ? new List<string> { PermissionMarkers.EmptyRolePermissionsMarker }
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
        foreach (var porte in DeduirePortes(permissions))
            await droitRepo.AddAsync(new DroitRole { IdRole = roleId, IdDroit = porte }, ct);
    }
}
