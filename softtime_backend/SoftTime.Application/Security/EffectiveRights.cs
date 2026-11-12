using SoftTime.Application.Services;

namespace SoftTime.Application.Security;

/// <summary>
/// Droits effectifs d'un utilisateur : droits du rôle, plus ses ajouts, moins ses retraits.
/// Objet immuable ; de nouvelles propriétés peuvent être ajoutées sans casser les appelants.
/// Se construit uniquement par <see cref="EffectiveRightsCalculator.ComputeEffectiveRights"/>.
/// </summary>
public sealed class EffectiveRights
{
    internal EffectiveRights() { }

    public bool IsAdministrator { get; internal init; }

    /// <summary>Rôle actuel du compte en base (null si le compte ou son rôle est introuvable).</summary>
    public decimal? RoleId { get; internal init; }

    /// <summary>Toutes les permissions effectives (comparaison insensible à la casse).</summary>
    public IReadOnlySet<string> Permissions { get; internal init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Portes 1 Bases, 2 Paramètres, 3 Traitements, 4 Autres, 5 Traçabilité, 6 Rapports.</summary>
    public IReadOnlySet<int> Gates { get; internal init; } = new HashSet<int>();

    /// <summary>Au moins un écart réel entre les droits effectifs et ceux du rôle.</summary>
    public bool IsCustomized { get; internal init; }

    public IReadOnlyList<RightsInconsistency> Inconsistencies { get; internal init; } = Array.Empty<RightsInconsistency>();

    /// <summary>Droits sensibles accordés : PARAMS:USERS, PARAMS:ROLES, PARAMS:PRIVILEGES, PARAMS:DB_ACCESS.</summary>
    public IReadOnlyList<string> SensitiveRights { get; internal init; } = Array.Empty<string>();

    /// <summary>Codes inconnus rencontrés (jamais acceptés).</summary>
    public IReadOnlyList<string> IgnoredPermissions { get; internal init; } = Array.Empty<string>();

    /// <summary>Lignes de l'utilisateur sans effet (ajout déjà détenu, retrait d'un droit absent, ajout annulé par un retrait).</summary>
    public IReadOnlyList<string> IneffectiveDeviations { get; internal init; } = Array.Empty<string>();

    public IReadOnlySet<string> KnownSageDatabases { get; internal init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> AuthorizedSageDatabases { get; internal init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool Has(string permission) => Permissions.Contains(permission);

    public bool HasGate(int gate) => Gates.Contains(gate);

    /// <summary>
    /// Utiliser une base. SAGE : porte 1, base cochée dans « Accès bases » ET SAGE:&lt;base&gt;:READ.
    /// Pointeuse : porte 1 ET POINTEUSE:&lt;base&gt;:READ — la règle exacte reste à décider, T_BDD_AUTORISE ne concerne que SAGE.
    /// L'administrateur utilise toute base connue.
    /// </summary>
    public bool CanUseDatabase(DatabaseKind kind, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (IsAdministrator)
            return kind == DatabaseKind.Sage ? KnownSageDatabases.Contains(name) : Has($"POINTEUSE:{name}:READ");
        if (!HasGate(1)) return false;
        return kind == DatabaseKind.Sage
            ? AuthorizedSageDatabases.Contains(name) && Has($"SAGE:{name}:READ")
            : Has($"POINTEUSE:{name}:READ");
    }

    /// <summary>Configurer une base : porte 1 ET permission fine (CREATE, UPDATE ou DELETE). Jamais nécessaire pour travailler.</summary>
    public bool CanConfigureDatabase(DatabaseKind kind, DatabaseConfigAction action, string? name = null)
    {
        if (IsAdministrator) return true;
        if (!HasGate(1)) return false;
        var prefix = kind == DatabaseKind.Sage ? "SAGE" : "POINTEUSE";
        if (action == DatabaseConfigAction.Create)
            return Has($"{prefix}:CREATE")
                   || Permissions.Any(p => p.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase)
                                           && p.EndsWith(":CREATE", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(name)) return false;
        return Has($"{prefix}:{name}:{(action == DatabaseConfigAction.Update ? "UPDATE" : "DELETE")}");
    }
}
