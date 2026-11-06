using SoftTime.Domain;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Security;

/// <summary>
/// Catalogue des permissions fines et des portes (fonctions pures, sans accès aux données).
/// DeduirePortes et BuildDefaultPermissionsList sont extraites d'AuthService sans changement de résultat
/// (voir PermissionCatalogCharacterizationTests).
/// </summary>
public static class PermissionCatalog
{
    public const string EmptyUserMarker = "__SOFTTIME_EMPTY_CUSTOM_PERMISSIONS__";
    public const string EmptyRoleMarker = "__SOFTTIME_EMPTY_ROLE_PERMISSIONS__";

    /// <summary>Permissions accordées par défaut avec chaque droit (porte) du rôle, dans l'ordre d'affichage.</summary>
    private static readonly (int Droit, string[] Codes)[] PermissionsParDroit =
    {
        (Rights.Parameters, new[]
        {
            "PARAMS:USERS", "PARAMS:USERS:CREATE", "PARAMS:USERS:UPDATE", "PARAMS:USERS:DELETE",
            "PARAMS:ROLES", "PARAMS:ROLES:CREATE", "PARAMS:ROLES:UPDATE", "PARAMS:ROLES:DELETE",
            "PARAMS:PRIVILEGES",
            "PARAMS:DB_ACCESS",
            "PARAMS:CLOCK_PARAMS",
            "PARAMS:CORRESPONDENCE",
            "PARAMS:CARDPAIE", "PARAMS:CARDPAIE:CREATE", "PARAMS:CARDPAIE:UPDATE", "PARAMS:CARDPAIE:DELETE",
            "PARAMS:CATEGORIES", "PARAMS:CATEGORIES:CREATE", "PARAMS:CATEGORIES:UPDATE", "PARAMS:CATEGORIES:DELETE",
            "PARAMS:AFFECTATIONS", "PARAMS:AFFECTATIONS:CREATE", "PARAMS:AFFECTATIONS:UPDATE", "PARAMS:AFFECTATIONS:DELETE",
            "PARAMS:TOLERANCES", "PARAMS:TOLERANCES:CREATE", "PARAMS:TOLERANCES:UPDATE",
            "PARAMS:HOLIDAYS", "PARAMS:HOLIDAYS:CREATE", "PARAMS:HOLIDAYS:UPDATE", "PARAMS:HOLIDAYS:DELETE",
            "PARAMS:MAJORATIONS", "PARAMS:MAJORATIONS:UPDATE",
            "PARAMS:CODE_CONSTANTES",
            "PARAMS:ABSENCE_CODES", "PARAMS:ABSENCE_CODES:CREATE", "PARAMS:ABSENCE_CODES:UPDATE"
        }),
        (Rights.Processing, new[]
        {
            "PROC:SHIFTS", "PROC:SHIFTS:CREATE", "PROC:SHIFTS:UPDATE", "PROC:SHIFTS:DELETE",
            "PROC:EMPLOYEE_PLANNING", "PROC:EMPLOYEE_PLANNING:CREATE", "PROC:EMPLOYEE_PLANNING:UPDATE", "PROC:EMPLOYEE_PLANNING:DELETE",
            "PROC:PUNCHES",
            "PROC:ANOMALIES", "PROC:ANOMALIES:DELETE",
            "PROC:CORRECTED_HOURS", "PROC:CORRECTED_HOURS:CREATE", "PROC:CORRECTED_HOURS:UPDATE", "PROC:CORRECTED_HOURS:DELETE",
            "PROC:WEEKLY_VALIDATION",
            "PROC:OVERTIME"
        }),
        (Rights.Other, new[] { "OTHER:CANTEEN", "OTHER:TRAVEL_EXPENSES" }),
        (Rights.Traceability, new[] { "TRACE:AUDIT" }),
        (Rights.Reports, new[] { "REPORTS:REPORTS", "REPORTS:PREDICTIONS", "REPORTS:CLOSING" })
    };

    /// <summary>Permissions statiques connues ; les codes SAGE: et POINTEUSE: dépendent des bases et sont acceptés par préfixe.</summary>
    public static readonly HashSet<string> KnownStatic =
        new(PermissionsParDroit.SelectMany(p => p.Codes), StringComparer.OrdinalIgnoreCase);

    public static bool IsKnown(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        if (code.StartsWith("SAGE:", StringComparison.OrdinalIgnoreCase) ||
            code.StartsWith("POINTEUSE:", StringComparison.OrdinalIgnoreCase))
            return true;
        return KnownStatic.Contains(code);
    }

    public static HashSet<decimal> DeduirePortes(IEnumerable<string> permCodes)
    {
        var portes = new HashSet<decimal>();
        foreach (var code in permCodes)
        {
            if (code.StartsWith("SAGE:", StringComparison.OrdinalIgnoreCase) ||
                code.StartsWith("POINTEUSE:", StringComparison.OrdinalIgnoreCase))
                portes.Add(Rights.Databases);
            else if (code.StartsWith("PARAMS:", StringComparison.OrdinalIgnoreCase))
                portes.Add(Rights.Parameters);
            else if (code.StartsWith("PROC:", StringComparison.OrdinalIgnoreCase))
                portes.Add(Rights.Processing);
            else if (code.StartsWith("OTHER:", StringComparison.OrdinalIgnoreCase))
                portes.Add(Rights.Other);
            else if (code.StartsWith("TRACE:", StringComparison.OrdinalIgnoreCase))
                portes.Add(Rights.Traceability);
            else if (code.StartsWith("REPORTS:", StringComparison.OrdinalIgnoreCase))
                portes.Add(Rights.Reports);
        }
        return portes;
    }

    public static List<string> BuildDefaultPermissionsList(
        HashSet<int> rights,
        IEnumerable<BddSage> sageDbs,
        IEnumerable<BddPointeuse> pointeuseDbs)
    {
        var defaults = new List<string>();
        var sageList = sageDbs as IReadOnlyCollection<BddSage> ?? sageDbs.ToList();
        if (sageList.Count > 0 || rights.Contains(Rights.Databases))
            defaults.Add("SAGE:CREATE");
        defaults.AddRange(DatabasePermissions("SAGE", sageList.Select(s => s.NomBd)));

        if (rights.Contains(Rights.Databases))
        {
            defaults.Add("POINTEUSE:CREATE");
            defaults.AddRange(DatabasePermissions("POINTEUSE", pointeuseDbs.Select(p => p.NomBd)));
        }

        foreach (var (droit, codes) in PermissionsParDroit)
        {
            if (rights.Contains(droit))
                defaults.AddRange(codes);
        }
        return defaults;
    }

    /// <summary>Lecture, modification et suppression de chaque base nommée (« SAGE:NomBase:READ »…).</summary>
    public static IEnumerable<string> DatabasePermissions(string prefix, IEnumerable<string?> noms) =>
        noms
            .Select(n => n ?? string.Empty)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .SelectMany(n => new[] { $"{prefix}:{n}:READ", $"{prefix}:{n}:UPDATE", $"{prefix}:{n}:DELETE" });
}
