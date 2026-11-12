using SoftTime.Application.Services;
using SoftTime.Domain;

namespace SoftTime.Application.Security;

/// <summary>
/// Calcul pur (sans accès aux données) des droits effectifs : lignes du rôle, plus les ajouts du compte, moins ses
/// retraits. Une action d'écran n'est effective qu'avec la page de son écran. Les portes se déduisent des droits
/// effectifs, en un seul endroit : un rôle sans ligne n'accorde rien.
/// </summary>
public static class EffectiveRightsCalculator
{
    public static readonly IReadOnlyList<string> SensitiveCodes =
        new[] { "users.read", "roles.read", "privileges.read", "db-access.read" };

    private const decimal AdminRoleId = 1;

    private static readonly int[] WorkGates = { Rights.Processing, Rights.Other, Rights.Reports };

    private static readonly Dictionary<int, string> GateLabels = new()
    {
        [Rights.Processing] = "Traitements",
        [Rights.Other] = "Autres",
        [Rights.Reports] = "Rapports",
    };

    public static EffectiveRights ComputeEffectiveRights(
        RoleDefinition role,
        IReadOnlyCollection<PermissionRow> userRows,
        DatabaseCatalog databases)
    {
        if (role.Id == AdminRoleId && role.Exists)
            return WithDatabases(Administrator(), databases);
        if (!role.Exists || role.Id is null)
            return WithDatabases(WithoutRole(), databases);

        // 1. Droits du rôle ; 2. écarts du compte ; 3. actions sans leur page retirées ; 4. portes déduites.
        var ignored = new List<string>();
        var roleBase = RoleBase(role, ignored);
        var (additions, removals, userMarker) = UserDeviations(userRows, ignored);
        var (start, effective) = Apply(roleBase, additions, removals, userMarker);
        var orphans = RemoveActionsWithoutPage(effective);
        var gates = PermissionCatalog.DeduirePortes(effective).Select(g => (int)g).ToHashSet();
        var authorized = databases.AuthorizedSage.ToHashSet();

        return WithDatabases(new EffectiveRights
        {
            Permissions = effective,
            Gates = gates,
            IsCustomized = !effective.SetEquals(roleBase),
            Inconsistencies = FindInconsistencies(gates, effective, authorized).Concat(orphans).ToList(),
            SensitiveRights = SensitiveCodes.Where(effective.Contains).ToList(),
            IgnoredPermissions = ignored.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            IneffectiveDeviations = IneffectiveDeviations(start, additions, removals),
        }, databases);
    }

    /// <summary>
    /// Migration des rôles historiques sans ligne : droits que leurs portes (T_DROIT_ROLE) accordaient, SANS la
    /// configuration des bases. La porte Bases donnait la lecture de toutes les bases, y compris celles ajoutées
    /// ensuite : elle devient le joker « sage.all.read » / « pointeuse.all.read ».
    /// </summary>
    public static IReadOnlyList<string> RightsFromLegacyGates(IReadOnlyCollection<int> gates)
        => PermissionCatalog.BuildDefaultPermissionsList(gates.ToHashSet())
            .Where(c => !DatabasePermissionCodes.IsConfiguration(c))
            .ToList();

    /// <summary>
    /// Écarts entre les droits d'un rôle et la liste complète voulue pour un compte : ajouts (dans la liste, pas dans
    /// le rôle) et retraits (dans le rôle, pas dans la liste). Codes inconnus écartés et signalés.
    /// </summary>
    public static LegacyConversion ConvertLegacyList(IReadOnlyCollection<string> roleEffective, IReadOnlyCollection<string> legacyList)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var role = new HashSet<string>(roleEffective.Where(c => !string.IsNullOrWhiteSpace(c)), comparer);
        var list = new HashSet<string>(comparer);
        var ignored = new List<string>();
        foreach (var code in legacyList)
        {
            if (string.IsNullOrWhiteSpace(code) || code == PermissionCatalog.EmptyUserMarker) continue;
            if (PermissionCatalog.IsKnown(code)) list.Add(code.Trim());
            else ignored.Add(code);
        }

        return new LegacyConversion(
            list.Where(c => !role.Contains(c)).OrderBy(c => c, comparer).ToList(),
            role.Where(c => !list.Contains(c)).OrderBy(c => c, comparer).ToList(),
            ignored.Distinct(comparer).ToList());
    }

    /// <summary>Bases déclarées (identifiant par nom) et bases SAGE cochées pour le compte.</summary>
    private static EffectiveRights WithDatabases(EffectiveRights rights, DatabaseCatalog databases) => new()
    {
        IsAdministrator = rights.IsAdministrator,
        Permissions = rights.Permissions,
        Gates = rights.Gates,
        IsCustomized = rights.IsCustomized,
        Inconsistencies = rights.Inconsistencies,
        SensitiveRights = rights.SensitiveRights,
        IgnoredPermissions = rights.IgnoredPermissions,
        IneffectiveDeviations = rights.IneffectiveDeviations,
        SageDatabases = ByName(databases.Sage),
        PointeuseDatabases = ByName(databases.Pointeuse),
        AuthorizedSageDatabases = databases.AuthorizedSage.ToHashSet(),
    };

    private static Dictionary<string, int> ByName(IEnumerable<DatabaseRef> bases)
    {
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in bases.Where(b => !string.IsNullOrWhiteSpace(b.Name)))
            byName.TryAdd(b.Name.Trim(), b.Id);
        return byName;
    }

    /// <summary>Droits de départ (aucun si le compte a le marqueur « aucun droit ») puis ajouts et retraits.</summary>
    private static (HashSet<string> Start, HashSet<string> Effective) Apply(
        HashSet<string> roleBase, HashSet<string> additions, HashSet<string> removals, bool userMarker)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var start = userMarker ? new HashSet<string>(comparer) : new HashSet<string>(roleBase, comparer);
        var effective = new HashSet<string>(start, comparer);
        effective.UnionWith(additions);
        effective.ExceptWith(removals);
        return (start, effective);
    }

    /// <summary>
    /// Une action d'écran (« categories.delete ») sans la page de son écran (« categories.read ») ne donne rien : elle
    /// est retirée et signalée (par exemple, page retirée au compte alors que le rôle garde l'action).
    /// </summary>
    private static List<RightsInconsistency> RemoveActionsWithoutPage(HashSet<string> effective)
    {
        var orphans = effective.Where(c => PermissionCatalog.PageOf(c) is string page && !effective.Contains(page)).ToList();
        effective.ExceptWith(orphans);
        return orphans.Select(c => new RightsInconsistency(
            "ACTION_WITHOUT_PAGE", 0, $"Le droit « {c} » est ignoré : il exige « {PermissionCatalog.PageOf(c)} »."))
            .ToList();
    }

    /// <summary>Sans rôle valide, aucun droit : les lignes du compte seules ne suffisent pas.</summary>
    private static EffectiveRights WithoutRole() => new()
    {
        Inconsistencies = new[] { new RightsInconsistency("ROLE_NOT_FOUND", 0, "Le rôle de l'utilisateur est introuvable : aucun droit n'est accordé.") },
    };

    /// <summary>Droits du rôle : ses lignes accordées connues (le marqueur « aucun droit » n'en est pas une).</summary>
    private static HashSet<string> RoleBase(RoleDefinition role, List<string> ignored)
    {
        var granted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in role.Rows.Where(r => r.IsGranted && r.Code != PermissionCatalog.EmptyRoleMarker))
        {
            if (PermissionCatalog.IsKnown(row.Code))
                granted.Add(row.Code.Trim());
            else
                ignored.Add(row.Code);
        }
        return granted;
    }

    /// <summary>Ajouts, retraits et marqueur « aucun droit » du compte ; codes inconnus écartés.</summary>
    private static (HashSet<string> Additions, HashSet<string> Removals, bool Marker) UserDeviations(
        IReadOnlyCollection<PermissionRow> userRows, List<string> ignored)
    {
        var additions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var userMarker = false;
        foreach (var row in userRows)
        {
            if (row.Code == PermissionCatalog.EmptyUserMarker)
                userMarker |= row.IsGranted;
            else if (!PermissionCatalog.IsKnown(row.Code))
                ignored.Add(row.Code);
            else
                (row.IsGranted ? additions : removals).Add(row.Code.Trim());
        }
        return (additions, removals, userMarker);
    }

    /// <summary>Écarts sans effet : ajout d'un droit déjà acquis (ou aussi retiré), retrait d'un droit absent.</summary>
    private static List<string> IneffectiveDeviations(HashSet<string> start, HashSet<string> additions, HashSet<string> removals) =>
        additions.Where(a => start.Contains(a) || removals.Contains(a)).Select(a => $"+{a}")
            .Concat(removals.Where(r => !start.Contains(r) && !additions.Contains(r)).Select(r => $"-{r}"))
            .ToList();

    /// <summary>L'administrateur a tous les droits, sur toutes les bases présentes et futures.</summary>
    private static EffectiveRights Administrator() => new()
    {
        IsAdministrator = true,
        Permissions = new HashSet<string>(
            PermissionCatalog.BuildDefaultPermissionsList(new HashSet<int> { 1, 2, 3, 4, 5, 6 }), StringComparer.OrdinalIgnoreCase),
        Gates = new HashSet<int> { 1, 2, 3, 4, 5, 6 },
        SensitiveRights = SensitiveCodes.ToList(),
    };

    private static List<RightsInconsistency> FindInconsistencies(HashSet<int> gates, HashSet<string> effective, HashSet<int> authorized)
    {
        var result = new List<RightsInconsistency>();
        var canUseASageDatabase = gates.Contains(Rights.Databases)
                                  && authorized.Any(id => DatabasePermissionCodes.Grants(effective, DatabaseKind.Sage, id, DatabasePermissionCodes.Read));
        if (canUseASageDatabase) return result;
        foreach (var gate in WorkGates.Where(gates.Contains))
            result.Add(new RightsInconsistency(
                "WORK_GATE_WITHOUT_DATABASE", gate,
                $"Le droit « {GateLabels[gate]} » est accordé sans accès à une base : cochez une base dans « Accès bases » et accordez-en la lecture."));
        return result;
    }
}
