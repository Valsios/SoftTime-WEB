using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Abstractions.Users;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services.Users;

/// <summary>
/// Droits d'un compte avec leur source, calculés comme le serveur les applique : droits du rôle seul, puis écarts du
/// compte. Un droit du rôle absent des droits effectifs est « retiré » ; un droit effectif hors rôle est « ajouté ».
/// Les ajouts d'un administrateur que le calcul ignore (ex. SELF:READ) sont listés comme non effectifs.
/// </summary>
public sealed class UserRightsDescriber : IUserRightsDescriber
{
    private readonly IUserPermissionService _permissions;
    private readonly IEffectiveRightsService _rights;
    private readonly IUnitOfWork _uow;

    public UserRightsDescriber(IUserPermissionService permissions, IEffectiveRightsService rights, IUnitOfWork uow)
    {
        _permissions = permissions;
        _rights = rights;
        _uow = uow;
    }

    public async Task<UserDetailedPermissionsDto> DescribeAsync(decimal userId, CancellationToken ct = default)
    {
        var shown = await _permissions.GetUserDetailedPermissionsAsync(userId, ct);
        var user = await _uow.Repository<Responsable>().GetByIdAsync(userId, ct)
            ?? throw new NotFoundException("Utilisateur introuvable.");
        var comparer = StringComparer.OrdinalIgnoreCase;
        var roleOnly = new HashSet<string>(await _rights.RoleDefaultPermissionsAsync(user.IdRole, ct), comparer);
        var effective = (await _rights.GetAsync(userId, fresh: true, ct: ct)).Permissions;
        var additions = (await _uow.Repository<UserPermission>()
                .ListReadOnlyAsync(p => p.IdResponsable == userId && p.IsGranted, ct))
            .Select(p => p.PermissionCode.Trim())
            .Where(PermissionCatalog.IsKnown);

        var rights = effective.Select(c => Describe(c, roleOnly.Contains(c) ? PermissionSources.Role : PermissionSources.Added, true))
            .Concat(roleOnly.Where(c => !effective.Contains(c)).Select(c => Describe(c, PermissionSources.Removed, false)))
            .Concat(additions.Where(c => !effective.Contains(c) && !roleOnly.Contains(c))
                .Select(c => Describe(c, PermissionSources.Added, false)))
            .DistinctBy(r => r.Code, comparer)
            .OrderBy(r => r.Code, comparer)
            .ToList();
        return shown with { Rights = rights };
    }

    private static EffectivePermissionDto Describe(string code, string source, bool isEffective)
        => new(code, source, isEffective, PermissionCodeValidator.IsLegacyDatabaseCode(code));
}
