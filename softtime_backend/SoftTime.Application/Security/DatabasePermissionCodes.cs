using SoftTime.Application.Services;

namespace SoftTime.Application.Security;

/// <summary>
/// Codes des droits par base, au format ressource.action, désignés par l'IDENTIFIANT de la base (un renommage ne fait
/// perdre aucun droit) : « sage.create », « sage.12.read », « pointeuse.3.update »… Le joker « all » vaut pour toutes
/// les bases du type, y compris celles déclarées plus tard : « sage.all.read ». Seul endroit qui fabrique ou décode ces codes.
/// </summary>
public static class DatabasePermissionCodes
{
    public const string Read = "read";
    public const string Update = "update";
    public const string Delete = "delete";
    public const string All = "all";
    private const string Create = "create";

    private static readonly string[] s_perDatabaseActions = { Read, Update, Delete };

    public static string Prefix(DatabaseKind kind) => kind == DatabaseKind.Sage ? "sage" : "pointeuse";

    /// <summary>Ajouter une base de ce type.</summary>
    public static string CreateCode(DatabaseKind kind) => $"{Prefix(kind)}.{Create}";

    /// <summary>Lire, modifier ou supprimer la base d'identifiant <paramref name="id"/>.</summary>
    public static string For(DatabaseKind kind, int id, string action) => $"{Prefix(kind)}.{id}.{action}";

    /// <summary>Même action sur toutes les bases du type, présentes et futures.</summary>
    public static string ForAll(DatabaseKind kind, string action) => $"{Prefix(kind)}.{All}.{action}";

    /// <summary>Lecture, modification et suppression de toutes les bases du type (joker).</summary>
    public static IEnumerable<string> AllActionsOnAll(DatabaseKind kind) => s_perDatabaseActions.Select(action => ForAll(kind, action));

    /// <summary>Vrai si les permissions accordent l'action sur cette base : par son identifiant ou par le joker.</summary>
    public static bool Grants(IReadOnlySet<string> permissions, DatabaseKind kind, int id, string action)
        => permissions.Contains(For(kind, id, action)) || permissions.Contains(ForAll(kind, action));

    public static string ActionOf(DatabaseConfigAction action) => action == DatabaseConfigAction.Update ? Update : Delete;

    /// <summary>Code de base (préfixe « sage. » ou « pointeuse. »), bien formé ou non.</summary>
    public static bool IsDatabaseCode(string code)
        => code.StartsWith("sage.", StringComparison.OrdinalIgnoreCase)
           || code.StartsWith("pointeuse.", StringComparison.OrdinalIgnoreCase);

    /// <summary>Configuration d'une base (création, modification, suppression), par opposition à sa lecture.</summary>
    public static bool IsConfiguration(string code)
        => TryParse(code, out _, out _, out var action) && action != Read;

    /// <summary>
    /// Décode un code de base : type, cible (identifiant en texte, « all », ou null pour « create ») et action.
    /// La cible doit être « all » ou un identifiant entier positif.
    /// </summary>
    public static bool TryParse(string code, out DatabaseKind kind, out string? target, out string action)
    {
        kind = default;
        target = null;
        action = string.Empty;
        if (string.IsNullOrWhiteSpace(code) || !IsDatabaseCode(code))
            return false;
        var parts = code.ToLowerInvariant().Split('.');
        kind = parts[0] == "sage" ? DatabaseKind.Sage : DatabaseKind.Pointeuse;
        if (parts.Length == 2)
        {
            action = parts[1];
            return action == Create;
        }
        if (parts.Length != 3 || !(parts[1] == All || (int.TryParse(parts[1], out var id) && id > 0)))
            return false;
        target = parts[1];
        action = parts[2];
        return s_perDatabaseActions.Contains(action);
    }

    /// <summary>Forme normalisée (tout en minuscules) d'un code de base bien formé ; inchangé sinon.</summary>
    public static string Normalize(string code)
        => TryParse(code, out _, out _, out _) ? code.Trim().ToLowerInvariant() : code;
}
