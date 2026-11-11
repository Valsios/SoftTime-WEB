using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Abstractions.Users;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Users;

/// <summary>
/// Droits d'un compte. L'écran manipule la liste complète des droits ; elle est stockée en écarts au rôle
/// (IS_GRANTED = 1 : ajout, 0 : retrait) : une modification du rôle atteint donc aussi les comptes personnalisés.
/// </summary>
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

    /// <summary>Droits effectifs du compte (même calcul que le contrôle serveur) et présence d'écarts au rôle.</summary>
    public async Task<UserDetailedPermissionsDto> GetUserDetailedPermissionsAsync(decimal userId, CancellationToken cancellationToken = default)
    {
        _ = await _uow.Repository<Responsable>().GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("Utilisateur introuvable.");
        var rights = await _effectiveRights.GetAsync(userId, fresh: true, ct: cancellationToken);
        return new UserDetailedPermissionsDto(userId, rights.Permissions.OrderBy(c => c, StringComparer.Ordinal).ToList(), rights.IsCustomized);
    }

    /// <summary>
    /// Enregistre la liste complète voulue pour un compte, sous forme d'écarts à son rôle. Une liste vide pose le marqueur
    /// « aucun droit » ; une liste égale aux droits du rôle n'enregistre aucun écart. Les droits d'un administrateur ne se
    /// modifient pas : il les a tous par son rôle.
    /// </summary>
    public async Task SaveUserDetailedPermissionsAsync(decimal currentUserId, decimal targetUserId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default)
    {
        var wanted = PermissionCodeNormalizer.Normalize(permissions);
        var caller = await RequireAdministratorCallerAsync(currentUserId, cancellationToken);
        var target = await _uow.Repository<Responsable>().GetByIdAsync(targetUserId, cancellationToken)
            ?? throw new NotFoundException("Utilisateur cible introuvable.");
        if (AdminRules.IsAdmin(target.IdRole))
            throw new DomainException("Un administrateur a tous les droits par son rôle : ils ne se modifient pas compte par compte.");

        var rows = await DeviationRowsAsync(targetUserId, wanted, cancellationToken);
        var permsAvant = await ReplaceUserPermissionsAsync(targetUserId, rows, cancellationToken);
        await _rightsChangeJournal.EcrireAuditDroitAsync(
            new RightsChange(currentUserId, caller.Login ?? string.Empty, RightsChangeActions.UserTarget, targetUserId, target.NomResponsable,
                "UPDATE_USER_PERMS", permsAvant, Describe(rows)),
            cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.Invalidate(targetUserId);
    }

    public async Task ResetUserDetailedPermissionsAsync(decimal currentUserId, decimal targetUserId, CancellationToken cancellationToken = default)
    {
        var caller = await RequireAdministratorCallerAsync(currentUserId, cancellationToken);
        var target = await _uow.Repository<Responsable>().GetByIdAsync(targetUserId, cancellationToken)
            ?? throw new NotFoundException("Utilisateur cible introuvable.");

        var permsAvant = await ReplaceUserPermissionsAsync(targetUserId, Array.Empty<UserPermission>(), cancellationToken);

        await _rightsChangeJournal.EcrireAuditDroitAsync(
            new RightsChange(currentUserId, caller.Login ?? string.Empty, RightsChangeActions.UserTarget, targetUserId, target.NomResponsable,
                "RESET_USER_PERMS", permsAvant, null),
            cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.Invalidate(targetUserId);
    }

    /// <summary>Lignes à stocker : marqueur « aucun droit » pour une liste vide, sinon ajouts et retraits par rapport au rôle.</summary>
    private async Task<List<UserPermission>> DeviationRowsAsync(decimal userId, List<string> wanted, CancellationToken ct)
    {
        if (wanted.Count == 0)
            return new List<UserPermission> { Row(userId, PermissionCatalog.EmptyUserMarker, true) };
        var roleOnly = await _effectiveRights.RoleOnlyAsync(userId, ct);
        var (additions, removals) = EffectiveRightsCalculator.ConvertLegacyList(roleOnly.Permissions.ToList(), wanted);
        return additions.Select(code => Row(userId, code, true))
            .Concat(removals.Select(code => Row(userId, code, false)))
            .ToList();
    }

    /// <summary>Administrateur exigé (403) : d'après le jeton, puis d'après le compte en base.</summary>
    private async Task<Responsable> RequireAdministratorCallerAsync(decimal currentUserId, CancellationToken ct)
    {
        _parametersAccessGuard.RequireAdministrator();
        var caller = await _uow.Repository<Responsable>().GetByIdAsync(currentUserId, ct)
            ?? throw new UnauthorizedException("Utilisateur connecté introuvable.");
        if (!AdminRules.IsSuperAdmin(caller.IdRole))
            throw new ForbiddenException("Seul un administrateur peut modifier les privilèges d'un compte.");
        return caller;
    }

    /// <summary>Remplace les lignes du compte et retourne celles d'avant (« +CODE », « -CODE ») pour le journal.</summary>
    private async Task<List<string>> ReplaceUserPermissionsAsync(
        decimal userId, IReadOnlyCollection<UserPermission> rows, CancellationToken ct)
    {
        var repo = _uow.Repository<UserPermission>();
        var existing = await repo.ListAsync(p => p.IdResponsable == userId, ct);
        var permsAvant = Describe(existing);
        repo.RemoveRange(existing);
        foreach (var row in rows)
            await repo.AddAsync(row, ct);
        return permsAvant;
    }

    private static UserPermission Row(decimal userId, string code, bool granted)
        => new() { IdResponsable = userId, PermissionCode = code, IsGranted = granted };

    private static List<string> Describe(IEnumerable<UserPermission> rows)
        => rows.Select(r => r.PermissionCode == PermissionCatalog.EmptyUserMarker
                ? "AUCUN_DROIT"
                : (r.IsGranted ? "+" : "-") + r.PermissionCode)
            .ToList();
}
