using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;
using SoftTime.Domain;

namespace SoftTime.Infrastructure.Services;

/// <summary>
/// Autorise la configuration (ajout, modification, suppression, test, découverte) des bases SAGE et pointeuse.
/// Hors administrateur, exige la porte Bases ET la permission fine, que le compte soit personnalisé ou non.
/// </summary>
public class DatabaseConfigurationAuthorizer : IDatabaseConfigurationAuthorizer
{
    private readonly ICurrentUser _user;
    private readonly IAuthService _auth;

    public DatabaseConfigurationAuthorizer(ICurrentUser user, IAuthService auth)
    {
        _user = user;
        _auth = auth;
    }

    public async Task EnsureCanConfigureAsync(DatabaseKind kind, DatabaseConfigAction action, string? dbName, CancellationToken ct = default)
    {
        if (AdminAccess.IsAdmin(_user)) return;
        var perms = await LoadPermissionsAsync(ct);
        var prefix = Prefix(kind);

        if (action == DatabaseConfigAction.Create)
        {
            if (!HasCreate(perms, prefix))
                throw new ForbiddenException($"Vous n'avez pas la permission d'ajouter une base {Label(kind)}.");
            return;
        }

        if (string.IsNullOrWhiteSpace(dbName))
            throw new ForbiddenException($"Base {Label(kind)} introuvable : opération refusée.");
        var code = $"{prefix}:{dbName}:{(action == DatabaseConfigAction.Update ? "UPDATE" : "DELETE")}";
        if (!perms.Contains(code))
        {
            var verbe = action == DatabaseConfigAction.Update ? "modifier" : "supprimer";
            throw new ForbiddenException($"Vous n'avez pas la permission de {verbe} la base {Label(kind)} '{dbName}'.");
        }
    }

    /// <summary>Test de connexion et découverte : il faut pouvoir ajouter ou modifier au moins une base.</summary>
    public async Task EnsureCanConfigureAnyAsync(DatabaseKind? kind, CancellationToken ct = default)
    {
        if (AdminAccess.IsAdmin(_user)) return;
        var perms = await LoadPermissionsAsync(ct);
        var kinds = kind.HasValue ? new[] { kind.Value } : new[] { DatabaseKind.Sage, DatabaseKind.Pointeuse };
        var allowed = kinds.Any(k =>
        {
            var prefix = Prefix(k);
            return HasCreate(perms, prefix)
                   || perms.Any(p => p.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase)
                                     && p.EndsWith(":UPDATE", StringComparison.OrdinalIgnoreCase));
        });
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

    private static bool HasCreate(HashSet<string> perms, string prefix)
        => perms.Contains($"{prefix}:CREATE")
           || perms.Any(p => p.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase)
                             && p.EndsWith(":CREATE", StringComparison.OrdinalIgnoreCase));

    private static string Prefix(DatabaseKind kind) => kind == DatabaseKind.Sage ? "SAGE" : "POINTEUSE";
    private static string Label(DatabaseKind kind) => kind == DatabaseKind.Sage ? "SAGE" : "pointeuse";
}
