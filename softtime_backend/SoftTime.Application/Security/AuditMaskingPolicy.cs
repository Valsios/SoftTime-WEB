namespace SoftTime.Application.Security;

/// <summary>
/// Liste unique des valeurs masquées dans le journal central <c>suivi_JOURNAL</c>.
/// <para>
/// Appliquée deux fois (défense en profondeur) : à l'écriture par l'intercepteur EF, et à la lecture par
/// <c>GET api/journal</c>, de sorte qu'une ligne ancienne stockée en clair ne sort jamais en clair.
/// </para>
/// <para>
/// Seules les valeurs sont masquées (<c>ANCIENS</c>, <c>NOUVEAUX</c>, et la valeur dans <c>CLES</c> lorsque la colonne
/// de clé est elle-même sensible) : le nom de la colonne reste visible dans <c>COLONNES</c>, pour que le fait
/// qu'elle a changé reste traçable. L'e-mail et le matricule restent visibles (décision fonctionnelle).
/// </para>
/// Toute nouvelle colonne secrète doit être ajoutée ici : le test <c>AuditMaskingPolicyTests</c> parcourt les entités
/// de <c>SoftTimeDbContext</c> et échoue si une colonne dont le nom évoque un secret n'est pas masquée.
/// </summary>
public static class AuditMaskingPolicy
{
    public const string Mask = "********";

    /// <summary>Colonnes masquées explicitement, par table (identifiants de connexion des bases, empreintes de mot de passe).</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ExplicitColumns =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["T_BDD_SAGE"] = Columns("TMDP", "TLOGIN", "SERVEUR"),
            ["T_BDD_POINTEUSE"] = Columns("TMDP", "TLOGIN", "SERVEUR"),
            ["T_RESPONSABLE"] = Columns("PASSWORD"),
        };

    /// <summary>
    /// Motifs génériques sur le nom de colonne, toutes tables : mots de passe, empreintes, jetons, clés d'API,
    /// clés privées et chaînes de connexion.
    /// </summary>
    public static readonly IReadOnlyList<string> SensitiveColumnPatterns =
    [
        "PASSWORD", "PASSWD", "PWD", "MDP", "SECRET", "TOKEN", "JETON", "API_KEY", "APIKEY", "PRIVATE",
        "HASH", "SALT", "CONNECTIONSTRING", "CONNECTION_STRING", "CHAINE_CONNEXION"
    ];

    /// <summary>Vrai si les valeurs de <paramref name="column"/> dans <paramref name="table"/> doivent être masquées.</summary>
    public static bool IsSensitive(string? table, string? column)
    {
        if (string.IsNullOrWhiteSpace(column))
            return false;
        var name = column.Trim();
        if (table is not null && ExplicitColumns.TryGetValue(table.Trim(), out var columns) && columns.Contains(name))
            return true;
        return SensitiveColumnPatterns.Any(pattern => name.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Valeur avant/après telle qu'elle peut sortir du journal : masquée si la colonne est sensible, <c>null</c> conservé.</summary>
    public static string? MaskValue(string? table, string? column, string? value)
        => value is not null && IsSensitive(table, column) ? Mask : value;

    /// <summary>
    /// Masque, dans une clé composée <c>COLONNE=valeur;COLONNE=valeur</c>, la valeur des colonnes sensibles.
    /// Une clé de forme inattendue est renvoyée telle quelle (les clés primaires ne sont jamais des secrets connus).
    /// </summary>
    public static string? MaskKey(string? table, string? key)
    {
        if (string.IsNullOrEmpty(key))
            return key;
        return string.Join(";", key.Split(';').Select(part =>
        {
            var separator = part.IndexOf('=');
            return separator > 0 && IsSensitive(table, part[..separator]) ? $"{part[..separator]}={Mask}" : part;
        }));
    }

    private static IReadOnlySet<string> Columns(params string[] names)
        => new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
}
