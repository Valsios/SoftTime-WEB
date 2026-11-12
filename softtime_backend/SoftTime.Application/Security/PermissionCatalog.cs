using SoftTime.Application.Services;
using SoftTime.Domain;

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
    public const string SelfRead = "self.read";

    /// <summary>Catégories et écrans, dans l'ordre d'affichage. Les bases (sage., pointeuse.) dépendent des bases déclarées, voir DatabasePermissionCodes.</summary>
    public static readonly IReadOnlyList<PermissionCategoryDefinition> Categories = new[]
    {
        new PermissionCategoryDefinition("PARAMS", "Paramètres", Rights.Parameters, new PermissionItemDefinition[]
        {
            new("users.read", "Utilisateurs", CanCreate: true, CanUpdate: true, CanDelete: true),
            new("roles.read", "Rôles", CanCreate: true, CanUpdate: true, CanDelete: true),
            new("privileges.read", "Privilèges"),
            new("db-access.read", "Accès bases"),
            new("clock-params.manage", "Paramètres pointeuse"),
            new("correspondence.manage", "Correspondance"),
            new("card-paie.read", "Correspondances paie", CanCreate: true, CanUpdate: true, CanDelete: true),
            new("categories.read", "Catégories", CanCreate: true, CanUpdate: true, CanDelete: true),
            new("affectations.read", "Affectations", CanCreate: true, CanUpdate: true, CanDelete: true),
            new("tolerances.read", "Tolérances", CanCreate: true, CanUpdate: true),
            new("holidays.read", "Jours fériés", CanCreate: true, CanUpdate: true, CanDelete: true),
            new("majorations.read", "Majorations", CanUpdate: true),
            new("code-constantes.manage", "Code Constante"),
            new("absence-codes.read", "Codes absence", CanCreate: true, CanUpdate: true),
        }),
        new PermissionCategoryDefinition("PROC", "Traitements", Rights.Processing, new PermissionItemDefinition[]
        {
            new("shifts.read", "Shifts", CanCreate: true, CanUpdate: true, CanDelete: true),
            new("employee-planning.read", "Planning salariés", CanCreate: true, CanUpdate: true, CanDelete: true),
            new("punches.manage", "Pointages"),
            new("anomalies.manage", "Anomalies", CanDelete: true),
            new("corrected-hours.read", "Heures corrigées", CanCreate: true, CanUpdate: true, CanDelete: true),
            new("weekly-validation.manage", "Validation semaine"),
            new("overtime.manage", "Heures supplémentaires"),
        }),
        new PermissionCategoryDefinition("OTHER", "Autres", Rights.Other, new PermissionItemDefinition[]
        {
            new("canteen.manage", "Cantine"),
            new("travel-expenses.manage", "Frais déplacement"),
        }),
        new PermissionCategoryDefinition("TRACE", "Traçabilités", Rights.Traceability, new PermissionItemDefinition[]
        {
            new("audit.read", "Audit et traçabilité"),
        }),
        new PermissionCategoryDefinition("REPORTS", "États", Rights.Reports, new PermissionItemDefinition[]
        {
            new("reports.read", "Rapports et exports"),
            new("predictions.read", "Prévisions"),
            new("closing.read", "Contrôle avant paie"),
        }),
        new PermissionCategoryDefinition("SELF", "Mon espace", null, new PermissionItemDefinition[]
        {
            new(SelfRead, "Consulter ses propres données (salarié)"),
        }),
    };

    /// <summary>Permissions statiques connues ; les codes de base dépendent des bases déclarées et sont validés par leur forme.</summary>
    public static readonly HashSet<string> KnownStatic = new(
        Categories.SelectMany(category => category.Items).SelectMany(item => item.Codes()), StringComparer.OrdinalIgnoreCase);

    /// <summary>Porte ouverte par chaque code statique (les codes sans porte, comme self.read, n'y figurent pas).</summary>
    private static readonly Dictionary<string, int> s_gateByCode = Categories
        .Where(category => category.Gate.HasValue)
        .SelectMany(category => category.Items.SelectMany(item => item.Codes()).Select(code => (code, gate: category.Gate!.Value)))
        .ToDictionary(pair => pair.code, pair => pair.gate, StringComparer.OrdinalIgnoreCase);

    /// <summary>Page de l'écran de chaque action (« categories.delete » -> « categories.read »).</summary>
    private static readonly Dictionary<string, string> s_pageByAction = Categories
        .SelectMany(category => category.Items)
        .SelectMany(item => item.Codes().Where(code => !string.Equals(code, item.Code, StringComparison.OrdinalIgnoreCase))
            .Select(action => (action, page: item.Code)))
        .ToDictionary(pair => pair.action, pair => pair.page, StringComparer.OrdinalIgnoreCase);

    /// <summary>Code connu : statique, ou code de base bien formé (« sage.create », « sage.12.read », « sage.all.read »).</summary>
    public static bool IsKnown(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        return IsDatabaseCode(code) ? DatabasePermissionCodes.TryParse(code, out _, out _, out _) : KnownStatic.Contains(code);
    }

    /// <summary>Code de base de données (préfixe « sage. » ou « pointeuse. »), bien formé ou non.</summary>
    public static bool IsDatabaseCode(string code) => DatabasePermissionCodes.IsDatabaseCode(code);

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
            if (s_gateByCode.TryGetValue(code, out var gate))
                portes.Add(gate);
        }
        return portes;
    }

    /// <summary>
    /// Droits accordés par défaut avec des portes : tous les écrans des catégories correspondantes ; avec la porte Bases,
    /// l'ajout et la lecture, modification, suppression de toutes les bases, présentes et futures (joker « all »).
    /// </summary>
    public static List<string> BuildDefaultPermissionsList(HashSet<int> rights)
    {
        var defaults = new List<string>();
        if (rights.Contains(Rights.Databases))
        {
            foreach (var kind in new[] { DatabaseKind.Sage, DatabaseKind.Pointeuse })
            {
                defaults.Add(DatabasePermissionCodes.CreateCode(kind));
                defaults.AddRange(DatabasePermissionCodes.AllActionsOnAll(kind));
            }
        }

        foreach (var category in Categories.Where(c => c.Gate is int gate && rights.Contains(gate)))
            defaults.AddRange(category.Items.SelectMany(item => item.Codes()));
        return defaults;
    }

    /// <summary>
    /// Page exigée par une action d'écran (« categories.read » pour « categories.delete ») ; null pour une page, un code
    /// de base ou un code inconnu. Les droits par base n'ont pas de dépendance : configurer une connexion n'exige pas d'en lire les données.
    /// </summary>
    public static string? PageOf(string code) => s_pageByAction.TryGetValue(code, out var page) ? page : null;
}
