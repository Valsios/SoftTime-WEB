using SoftTime.Application.Services;

namespace SoftTime.Application.Security;

/// <summary>
/// Droits effectifs d'un utilisateur : droits du rôle, plus ses ajouts, moins ses retraits.
/// Objet immuable ; de nouvelles propriétés peuvent être ajoutées sans casser les appelants.
/// Se construit uniquement par <see cref="EffectiveRightsCalculator.ComputeEffectiveRights"/>.
/// </summary>
public sealed class EffectiveRights
{
    private string? _stamp;

    internal EffectiveRights() { }

    public bool IsAdministrator { get; internal init; }

    /// <summary>Toutes les permissions effectives (comparaison insensible à la casse).</summary>
    public IReadOnlySet<string> Permissions { get; internal init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Portes 1 Bases, 2 Paramètres, 3 Traitements, 4 Autres, 5 Traçabilité, 6 Rapports.</summary>
    public IReadOnlySet<int> Gates { get; internal init; } = new HashSet<int>();

    /// <summary>Au moins un écart réel entre les droits effectifs et ceux du rôle.</summary>
    public bool IsCustomized { get; internal init; }

    public IReadOnlyList<RightsInconsistency> Inconsistencies { get; internal init; } = Array.Empty<RightsInconsistency>();

    /// <summary>Droits sensibles accordés : users.read, roles.read, privileges.read, db-access.read.</summary>
    public IReadOnlyList<string> SensitiveRights { get; internal init; } = Array.Empty<string>();

    /// <summary>Codes inconnus rencontrés (jamais acceptés).</summary>
    public IReadOnlyList<string> IgnoredPermissions { get; internal init; } = Array.Empty<string>();

    /// <summary>Lignes de l'utilisateur sans effet (ajout déjà détenu, retrait d'un droit absent, ajout annulé par un retrait).</summary>
    public IReadOnlyList<string> IneffectiveDeviations { get; internal init; } = Array.Empty<string>();

    /// <summary>Bases SAGE déclarées : identifiant par nom (sans casse).</summary>
    public IReadOnlyDictionary<string, int> SageDatabases { get; internal init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bases pointeuse déclarées : identifiant par nom (sans casse).</summary>
    public IReadOnlyDictionary<string, int> PointeuseDatabases { get; internal init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Identifiants des bases SAGE cochées dans « Accès bases » pour le compte.</summary>
    public IReadOnlySet<int> AuthorizedSageDatabases { get; internal init; } = new HashSet<int>();

    /// <summary>Empreinte de ces droits, écrite dans le jeton d'accès (voir <see cref="RightsStamp"/>).</summary>
    public string Stamp => _stamp ??= RightsStamp.Compute(this);

    /// <summary>
    /// Permissions écrites dans le jeton : les permissions fixes seulement. Les droits par base (sage., pointeuse.)
    /// restent contrôlés côté serveur ; l'administrateur passe par son rôle et n'en porte aucune.
    /// </summary>
    public IReadOnlyList<string> TokenPermissions => IsAdministrator
        ? Array.Empty<string>()
        : Permissions.Where(code => !DatabasePermissionCodes.IsDatabaseCode(code)).OrderBy(code => code, StringComparer.Ordinal).ToList();

    public bool Has(string permission) => Permissions.Contains(permission);

    public bool HasGate(int gate) => Gates.Contains(gate);

    /// <summary>
    /// Utiliser une base désignée par son nom (en-tête de la requête). SAGE : porte 1, base cochée dans « Accès bases »
    /// ET lecture de la base (sage.&lt;id&gt;.read ou sage.all.read). Pointeuse : porte 1 ET lecture de la base
    /// (T_BDD_AUTORISE ne concerne que SAGE).
    /// Une base inconnue n'est jamais utilisable ; l'administrateur utilise toute base connue.
    /// </summary>
    public bool CanUseDatabase(DatabaseKind kind, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !TryGetId(kind, name, out var id)) return false;
        if (IsAdministrator) return true;
        if (!HasGate(1)) return false;
        if (kind == DatabaseKind.Sage && !AuthorizedSageDatabases.Contains(id)) return false;
        return DatabasePermissionCodes.Grants(Permissions, kind, id, DatabasePermissionCodes.Read);
    }

    /// <summary>Configurer une base : porte 1 ET permission fine (CREATE, UPDATE ou DELETE). Jamais nécessaire pour travailler.</summary>
    public bool CanConfigureDatabase(DatabaseKind kind, DatabaseConfigAction action, string? name = null)
    {
        if (IsAdministrator) return true;
        if (!HasGate(1)) return false;
        if (action == DatabaseConfigAction.Create)
            return Has(DatabasePermissionCodes.CreateCode(kind));
        if (string.IsNullOrWhiteSpace(name) || !TryGetId(kind, name, out var id)) return false;
        return DatabasePermissionCodes.Grants(Permissions, kind, id, DatabasePermissionCodes.ActionOf(action));
    }

    private bool TryGetId(DatabaseKind kind, string name, out int id)
        => (kind == DatabaseKind.Sage ? SageDatabases : PointeuseDatabases).TryGetValue(name.Trim(), out id);
}
