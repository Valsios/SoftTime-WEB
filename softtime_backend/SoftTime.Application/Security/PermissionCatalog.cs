using SoftTime.Domain;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Security;

/// <summary>
/// Catalogue unique des permissions fines et des portes (fonctions pures, sans accès aux données). Le serveur s'en sert
/// pour valider et accorder les droits ; l'écran Privilèges le lit par <c>GET /api/v1/privileges/catalog</c>.
/// DeduirePortes et BuildDefaultPermissionsList gardent le résultat d'origine (voir PermissionCatalogCharacterizationTests).
/// </summary>
public static class PermissionCatalog
{
    public const string EmptyUserMarker = "__SOFTTIME_EMPTY_CUSTOM_PERMISSIONS__";
    public const string EmptyRoleMarker = "__SOFTTIME_EMPTY_ROLE_PERMISSIONS__";

    /// <summary>Mon espace : lire ses propres données, sans aucune porte.</summary>
    public const string SelfRead = "SELF:READ";

    private const string SagePrefix = "SAGE";
    private const string PointeusePrefix = "POINTEUSE";
    private static readonly string[] s_databaseActions = { "READ", "UPDATE", "DELETE" };

    /// <summary>Catégories et écrans, dans l'ordre d'affichage. Les bases (SAGE:, POINTEUSE:) dépendent des bases déclarées.</summary>
    public static readonly IReadOnlyList<PermissionCategoryDefinition> Categories = new[]
    {
        new PermissionCategoryDefinition("PARAMS", "Paramètres", Rights.Parameters, new PermissionItemDefinition[]
        {
            new("PARAMS:USERS", "Utilisateurs", CanCreate: true, CanUpdate: true, CanDelete: true),
            new("PARAMS:ROLES", "Rôles", CanCreate: true, CanUpdate: true, CanDelete: true),
            new("PARAMS:PRIVILEGES", "Privilèges"),
            new("PARAMS:DB_ACCESS", "Accès bases"),
            new("PARAMS:CLOCK_PARAMS", "Paramètres pointeuse"),
            new("PARAMS:CORRESPONDENCE", "Correspondance"),
            new("PARAMS:CARDPAIE", "Correspondances paie", CanCreate: true, CanUpdate: true, CanDelete: true),
            new("PARAMS:CATEGORIES", "Catégories", CanCreate: true, CanUpdate: true, CanDelete: true),
            new("PARAMS:AFFECTATIONS", "Affectations", CanCreate: true, CanUpdate: true, CanDelete: true),
            new("PARAMS:TOLERANCES", "Tolérances", CanCreate: true, CanUpdate: true),
            new("PARAMS:HOLIDAYS", "Jours fériés", CanCreate: true, CanUpdate: true, CanDelete: true),
            new("PARAMS:MAJORATIONS", "Majorations", CanUpdate: true),
            new("PARAMS:CODE_CONSTANTES", "Code Constante"),
            new("PARAMS:ABSENCE_CODES", "Codes absence", CanCreate: true, CanUpdate: true),
        }),
        new PermissionCategoryDefinition("PROC", "Traitements", Rights.Processing, new PermissionItemDefinition[]
        {
            new("PROC:SHIFTS", "Shifts", CanCreate: true, CanUpdate: true, CanDelete: true),
            new("PROC:EMPLOYEE_PLANNING", "Planning salariés", CanCreate: true, CanUpdate: true, CanDelete: true),
            new("PROC:PUNCHES", "Pointages"),
            new("PROC:ANOMALIES", "Anomalies", CanDelete: true),
            new("PROC:CORRECTED_HOURS", "Heures corrigées", CanCreate: true, CanUpdate: true, CanDelete: true),
            new("PROC:WEEKLY_VALIDATION", "Validation semaine"),
            new("PROC:OVERTIME", "Heures supplémentaires"),
        }),
        new PermissionCategoryDefinition("OTHER", "Autres", Rights.Other, new PermissionItemDefinition[]
        {
            new("OTHER:CANTEEN", "Cantine"),
            new("OTHER:TRAVEL_EXPENSES", "Frais déplacement"),
        }),
        new PermissionCategoryDefinition("TRACE", "Traçabilités", Rights.Traceability, new PermissionItemDefinition[]
        {
            new("TRACE:AUDIT", "Audit et traçabilité"),
        }),
        new PermissionCategoryDefinition("REPORTS", "États", Rights.Reports, new PermissionItemDefinition[]
        {
            new("REPORTS:REPORTS", "Rapports et exports"),
            new("REPORTS:PREDICTIONS", "Prévisions"),
            new("REPORTS:CLOSING", "Contrôle avant paie"),
        }),
        new PermissionCategoryDefinition("SELF", "Mon espace", null, new PermissionItemDefinition[]
        {
            new(SelfRead, "Consulter ses propres données (salarié)"),
        }),
    };

    /// <summary>Permissions statiques connues ; les codes SAGE: et POINTEUSE: dépendent des bases et sont validés par leur forme.</summary>
    public static readonly HashSet<string> KnownStatic = new(
        Categories.SelectMany(category => category.Items).SelectMany(item => item.Codes()), StringComparer.OrdinalIgnoreCase);

    /// <summary>Code connu : statique, ou code de base bien formé (« SAGE:CREATE », « SAGE:NomBase:READ »…).</summary>
    public static bool IsKnown(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        return IsDatabaseCode(code) ? IsWellFormedDatabaseCode(code) : KnownStatic.Contains(code);
    }

    /// <summary>Code de base de données (préfixe SAGE: ou POINTEUSE:), bien formé ou non.</summary>
    public static bool IsDatabaseCode(string code)
        => code.StartsWith(SagePrefix + ":", StringComparison.OrdinalIgnoreCase)
           || code.StartsWith(PointeusePrefix + ":", StringComparison.OrdinalIgnoreCase);

    public static HashSet<decimal> DeduirePortes(IEnumerable<string> permCodes)
    {
        var portes = new HashSet<decimal>();
        foreach (var code in permCodes)
        {
            if (IsDatabaseCode(code))
            {
                portes.Add(Rights.Databases);
                continue;
            }
            var gate = Categories.FirstOrDefault(c => c.Gate.HasValue && code.StartsWith(c.Key + ":", StringComparison.OrdinalIgnoreCase))?.Gate;
            if (gate is int g)
                portes.Add(g);
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
        defaults.AddRange(DatabasePermissions(SagePrefix, sageList.Select(s => s.NomBd)));

        if (rights.Contains(Rights.Databases))
        {
            defaults.Add("POINTEUSE:CREATE");
            defaults.AddRange(DatabasePermissions(PointeusePrefix, pointeuseDbs.Select(p => p.NomBd)));
        }

        foreach (var category in Categories.Where(c => c.Gate is int gate && rights.Contains(gate)))
            defaults.AddRange(category.Items.SelectMany(item => item.Codes()));
        return defaults;
    }

    /// <summary>Lecture, modification et suppression de chaque base nommée (« SAGE:NomBase:READ »…).</summary>
    public static IEnumerable<string> DatabasePermissions(string prefix, IEnumerable<string?> noms) =>
        noms
            .Select(n => n ?? string.Empty)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .SelectMany(n => s_databaseActions.Select(action => $"{prefix}:{n}:{action}"));

    private static bool IsWellFormedDatabaseCode(string code)
    {
        var parts = code.Split(':');
        return parts.Length switch
        {
            2 => parts[1].Equals("CREATE", StringComparison.OrdinalIgnoreCase),
            3 => !string.IsNullOrWhiteSpace(parts[1])
                 && s_databaseActions.Contains(parts[2], StringComparer.OrdinalIgnoreCase),
            _ => false
        };
    }
}
