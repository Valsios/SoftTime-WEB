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

    /// <summary>Permissions statiques connues ; les codes SAGE: et POINTEUSE: dépendent des bases et sont acceptés par préfixe.</summary>
    public static readonly HashSet<string> KnownStatic = new(StringComparer.OrdinalIgnoreCase)
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
        "SELF:READ",   // Mon espace : lire ses propres donnees, sans aucune porte
        "PROC:CORRECTED_HOURS", "PROC:CORRECTED_HOURS:CREATE", "PROC:CORRECTED_HOURS:UPDATE", "PROC:CORRECTED_HOURS:DELETE",
        "PROC:WEEKLY_VALIDATION", "PROC:OVERTIME",
        "OTHER:CANTEEN", "OTHER:TRAVEL_EXPENSES",
        "TRACE:AUDIT",
        "REPORTS:REPORTS", "REPORTS:PREDICTIONS", "REPORTS:CLOSING"
    };

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
        IEnumerable<T_BDD_SAGE> sageDbs,
        IEnumerable<T_BDD_POINTEUSE> pointeuseDbs)
    {
        var defaults = new List<string>();
        var sageList = sageDbs as IReadOnlyCollection<T_BDD_SAGE> ?? sageDbs.ToList();

        if (sageList.Count > 0 || rights.Contains(Rights.Databases))
        {
            defaults.Add("SAGE:CREATE");
        }

        foreach (var s in sageList)
        {
            var name = s.NOM_BD ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name)) continue;
            defaults.Add($"SAGE:{name}:READ");
            defaults.Add($"SAGE:{name}:UPDATE");
            defaults.Add($"SAGE:{name}:DELETE");
        }

        if (rights.Contains(Rights.Databases))
        {
            defaults.Add("POINTEUSE:CREATE");
            foreach (var p in pointeuseDbs)
            {
                var name = p.NOM_BD ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name)) continue;
                defaults.Add($"POINTEUSE:{name}:READ");
                defaults.Add($"POINTEUSE:{name}:UPDATE");
                defaults.Add($"POINTEUSE:{name}:DELETE");
            }
        }

        if (rights.Contains(Rights.Parameters))
        {
            defaults.AddRange(new[]
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
            });
        }

        if (rights.Contains(Rights.Processing))
        {
            defaults.AddRange(new[]
            {
                "PROC:SHIFTS", "PROC:SHIFTS:CREATE", "PROC:SHIFTS:UPDATE", "PROC:SHIFTS:DELETE",
                "PROC:EMPLOYEE_PLANNING", "PROC:EMPLOYEE_PLANNING:CREATE", "PROC:EMPLOYEE_PLANNING:UPDATE", "PROC:EMPLOYEE_PLANNING:DELETE",
                "PROC:PUNCHES",
                "PROC:ANOMALIES", "PROC:ANOMALIES:DELETE",
                "PROC:CORRECTED_HOURS", "PROC:CORRECTED_HOURS:CREATE", "PROC:CORRECTED_HOURS:UPDATE", "PROC:CORRECTED_HOURS:DELETE",
                "PROC:WEEKLY_VALIDATION",
                "PROC:OVERTIME"
            });
        }

        if (rights.Contains(Rights.Other))
        {
            defaults.AddRange(new[]
            {
                "OTHER:CANTEEN", "OTHER:TRAVEL_EXPENSES"
            });
        }

        if (rights.Contains(Rights.Traceability))
        {
            defaults.Add("TRACE:AUDIT");
        }

        if (rights.Contains(Rights.Reports))
        {
            defaults.Add("REPORTS:REPORTS");
            defaults.Add("REPORTS:PREDICTIONS");
            defaults.Add("REPORTS:CLOSING");
        }

        return defaults;
    }
}
