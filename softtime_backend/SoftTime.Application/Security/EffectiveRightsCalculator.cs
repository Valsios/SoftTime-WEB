using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain;

namespace SoftTime.Application.Security;

/// <summary>
/// Calcul pur (sans accès aux données) des droits effectifs : droits du rôle, plus les ajouts de l'utilisateur,
/// moins ses retraits. Les portes se déduisent des droits effectifs, en un seul endroit.
/// Non branché : aucun appelant en production pour l'instant.
/// </summary>
public static class EffectiveRightsCalculator
{
    public static readonly IReadOnlyList<string> SensitiveCodes =
        new[] { "PARAMS:USERS", "PARAMS:ROLES", "PARAMS:PRIVILEGES", "PARAMS:DB_ACCESS" };

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
        var comparer = StringComparer.OrdinalIgnoreCase;
        var knownSage = new HashSet<string>(Clean(databases.Sage), comparer);
        var knownPointeuse = new HashSet<string>(Clean(databases.Pointeuse), comparer);
        var authorized = new HashSet<string>(Clean(databases.AuthorizedSage), comparer);

        if (role.Id == AdminRoleId && role.Exists)
            return Administrator(knownSage, knownPointeuse, authorized);

        if (!role.Exists || role.Id is null)
        {
            // Sans rôle valide, aucun droit : les lignes de l'utilisateur seules ne suffisent pas.
            return new EffectiveRights
            {
                KnownSageDatabases = knownSage,
                AuthorizedSageDatabases = authorized,
                Inconsistencies = new[] { new RightsInconsistency("ROLE_NOT_FOUND", 0, "Le rôle de l'utilisateur est introuvable : aucun droit n'est accordé.") },
            };
        }

        var ignored = new List<string>();

        // 1. Droits du rôle.
        var roleGranted = new HashSet<string>(comparer);
        var roleHasMarker = false;
        foreach (var row in role.Rows)
        {
            if (!row.IsGranted) continue;
            if (row.Code == PermissionCatalog.EmptyRoleMarker) { roleHasMarker = true; continue; }
            if (!PermissionCatalog.IsKnown(row.Code)) { ignored.Add(row.Code); continue; }
            roleGranted.Add(row.Code.Trim());
        }

        var hasDetail = roleGranted.Count > 0 || roleHasMarker;
        var roleGates = role.Gates.ToHashSet();
        var roleBase = hasDetail
            ? roleGranted
            : new HashSet<string>(DefaultsWithoutConfiguration(roleGates, knownSage, knownPointeuse), comparer);

        // 2. Écarts de l'utilisateur.
        var additions = new HashSet<string>(comparer);
        var removals = new HashSet<string>(comparer);
        var userMarker = false;
        foreach (var row in userRows)
        {
            if (row.Code == PermissionCatalog.EmptyUserMarker) { if (row.IsGranted) userMarker = true; continue; }
            if (!PermissionCatalog.IsKnown(row.Code)) { ignored.Add(row.Code); continue; }
            (row.IsGranted ? additions : removals).Add(row.Code.Trim());
        }

        var start = userMarker ? new HashSet<string>(comparer) : new HashSet<string>(roleBase, comparer);
        var effective = new HashSet<string>(start, comparer);
        effective.UnionWith(additions);
        effective.ExceptWith(removals);

        var ineffective = new List<string>();
        foreach (var a in additions.Where(a => start.Contains(a) || removals.Contains(a)))
            ineffective.Add($"+{a}");
        foreach (var r in removals.Where(r => !start.Contains(r) && !additions.Contains(r)))
            ineffective.Add($"-{r}");

        // 3. Portes déduites des droits effectifs. Un rôle sans lignes détaillées conserve ses portes tant que
        //    l'utilisateur n'en retire pas les droits (cas d'une porte Bases sans aucune base déclarée).
        var gates = PermissionCatalog.DeduirePortes(effective).Select(g => (int)g).ToHashSet();
        if (!hasDetail && !userMarker)
            foreach (var g in roleGates.Where(g => !removals.Any(r => GateOfCode(r) == g)))
                gates.Add(g);

        return new EffectiveRights
        {
            Permissions = effective,
            Gates = gates,
            IsCustomized = !effective.SetEquals(roleBase),
            Inconsistencies = FindInconsistencies(gates, effective, authorized),
            SensitiveRights = SensitiveCodes.Where(effective.Contains).ToList(),
            IgnoredPermissions = ignored.Distinct(comparer).ToList(),
            IneffectiveDeviations = ineffective,
            KnownSageDatabases = knownSage,
            AuthorizedSageDatabases = authorized,
        };
    }

    /// <summary>
    /// Migration future (simulation) : écarts entre les droits effectifs du rôle et la liste complète actuelle de l'utilisateur.
    /// Une liste vide, ou réduite au marqueur « aucun droit », retire tous les droits du rôle.
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

    private const decimal AdminRoleId = 1;

    private static EffectiveRights Administrator(HashSet<string> sage, HashSet<string> pointeuse, HashSet<string> authorized)
    {
        var all = PermissionCatalog.BuildDefaultPermissionsList(
            new HashSet<int> { 1, 2, 3, 4, 5, 6 },
            sage.Select(n => new T_BDD_SAGE { NOM_BD = n }),
            pointeuse.Select(n => new T_BDD_POINTEUSE { NOM_BD = n }));
        var permissions = new HashSet<string>(all, StringComparer.OrdinalIgnoreCase);
        return new EffectiveRights
        {
            IsAdministrator = true,
            Permissions = permissions,
            Gates = new HashSet<int> { 1, 2, 3, 4, 5, 6 },
            SensitiveRights = SensitiveCodes.ToList(),
            KnownSageDatabases = sage,
            AuthorizedSageDatabases = authorized,
        };
    }

    /// <summary>
    /// Droits par défaut des portes d'un rôle sans lignes détaillées. Même liste que BuildDefaultPermissionsList,
    /// SANS la configuration des bases (CREATE, UPDATE, DELETE sur SAGE: et POINTEUSE:), et sans droits sur les bases
    /// si la porte 1 n'est pas donnée.
    /// </summary>
    private static IEnumerable<string> DefaultsWithoutConfiguration(HashSet<int> gates, HashSet<string> sage, HashSet<string> pointeuse)
    {
        var hasDatabases = gates.Contains(Rights.Databases);
        var defaults = PermissionCatalog.BuildDefaultPermissionsList(
            gates,
            hasDatabases ? sage.Select(n => new T_BDD_SAGE { NOM_BD = n }) : Enumerable.Empty<T_BDD_SAGE>(),
            hasDatabases ? pointeuse.Select(n => new T_BDD_POINTEUSE { NOM_BD = n }) : Enumerable.Empty<T_BDD_POINTEUSE>());
        return defaults.Where(c => !IsDatabaseConfiguration(c));
    }

    private static bool IsDatabaseConfiguration(string code)
    {
        var isDb = code.StartsWith("SAGE:", StringComparison.OrdinalIgnoreCase) || code.StartsWith("POINTEUSE:", StringComparison.OrdinalIgnoreCase);
        return isDb && (code.EndsWith(":CREATE", StringComparison.OrdinalIgnoreCase)
                        || code.EndsWith(":UPDATE", StringComparison.OrdinalIgnoreCase)
                        || code.EndsWith(":DELETE", StringComparison.OrdinalIgnoreCase));
    }

    private static List<RightsInconsistency> FindInconsistencies(HashSet<int> gates, HashSet<string> effective, HashSet<string> authorized)
    {
        var result = new List<RightsInconsistency>();
        var canUseASageDatabase = gates.Contains(Rights.Databases)
                                  && authorized.Any(b => effective.Contains($"SAGE:{b}:READ"));
        if (canUseASageDatabase) return result;
        foreach (var gate in WorkGates.Where(gates.Contains))
            result.Add(new RightsInconsistency(
                "WORK_GATE_WITHOUT_DATABASE", gate,
                $"Le droit « {GateLabels[gate]} » est accordé sans accès à une base : cochez une base dans « Accès bases » et accordez-en la lecture."));
        return result;
    }

    private static int GateOfCode(string code)
    {
        var gates = PermissionCatalog.DeduirePortes(new[] { code });
        return gates.Count == 0 ? 0 : (int)gates.First();
    }

    private static IEnumerable<string> Clean(IEnumerable<string> names)
        => names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim());
}
