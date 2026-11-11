using SoftTime.Application.Abstractions.DataSources;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Abstractions.Users;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain;

namespace SoftTime.Infrastructure.Services.DataSources;

/// <summary>
/// Autorise la configuration (ajout, modification, suppression, test, découverte) des bases SAGE et pointeuse.
/// Hors administrateur, exige la porte Bases ET la permission fine, que le compte soit personnalisé ou non.
/// </summary>
public class DatabaseConfigurationAuthorizer : IDatabaseConfigurationAuthorizer
{
    private readonly ICurrentUser _user;
    private readonly IUserPermissionService _auth;

    public DatabaseConfigurationAuthorizer(ICurrentUser user, IUserPermissionService auth)
    {
        _user = user;
        _auth = auth;
    }

    public async Task EnsureCanConfigureAsync(DatabaseKind kind, DatabaseConfigAction action, DatabaseRef? target, CancellationToken ct = default)
    {
        if (AdminAccess.IsAdmin(_user)) return;
        var perms = await LoadPermissionsAsync(ct);
        if (action == DatabaseConfigAction.Create)
        {
            if (!perms.Contains(DatabasePermissionCodes.CreateCode(kind)))
                throw new ForbiddenException($"Vous n'avez pas la permission d'ajouter une base {Label(kind)}.");
            return;
        }

        if (target is null || target.Id <= 0)
            throw new ForbiddenException($"Base {Label(kind)} introuvable : opération refusée.");
        if (!DatabasePermissionCodes.Grants(perms, kind, target.Id, DatabasePermissionCodes.ActionOf(action)))
        {
            var verbe = action == DatabaseConfigAction.Update ? "modifier" : "supprimer";
            throw new ForbiddenException($"Vous n'avez pas la permission de {verbe} la base {Label(kind)} '{target.Name}'.");
        }
    }

    /// <summary>Test de connexion et découverte : il faut pouvoir ajouter ou modifier au moins une base.</summary>
    public async Task EnsureCanConfigureAnyAsync(DatabaseKind? kind, CancellationToken ct = default)
    {
        if (AdminAccess.IsAdmin(_user)) return;
        var perms = await LoadPermissionsAsync(ct);
        var kinds = kind.HasValue ? new[] { kind.Value } : new[] { DatabaseKind.Sage, DatabaseKind.Pointeuse };
        var allowed = kinds.Any(k => perms.Contains(DatabasePermissionCodes.CreateCode(k))
            || perms.Any(p => DatabasePermissionCodes.TryParse(p, out var pk, out _, out var action)
                              && pk == k && action == DatabasePermissionCodes.Update));
        if (!allowed)
            throw new ForbiddenException("Vous n'avez pas la permission de configurer les bases.");
    }

    private async Task<HashSet<string>> LoadPermissionsAsync(CancellationToken ct)
    {
        if (_user.UserId is not decimal uid)
            throw new ForbiddenException("Utilisateur non identifié.");
        if (!_user.HasRight(Rights.Databases))
            throw new ForbiddenException("La configuration des bases exige le droit « Bases ».");
        try
        {
            var detailed = await _auth.GetUserDetailedPermissionsAsync(uid, ct);
            return new HashSet<string>(detailed.GrantedPermissions, StringComparer.OrdinalIgnoreCase);
        }
        catch (NotFoundException)
        {
            throw new ForbiddenException("Utilisateur introuvable.");
        }
    }

    private static string Label(DatabaseKind kind) => kind == DatabaseKind.Sage ? "SAGE" : "pointeuse";
}
