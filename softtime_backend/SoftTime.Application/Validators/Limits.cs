namespace SoftTime.Application.Validators;

/// <summary>
/// Bornes communes des champs saisis. Les règles métier (champ requis, politique de mot de passe, login déjà pris)
/// restent dans les services, qui renvoient leurs propres messages ; ces validateurs refusent seulement les valeurs
/// démesurées ou mal formées avant tout traitement (exécutés par FluentValidationFilter dans l'API).
/// </summary>
internal static class Limits
{
    public const int Name = 100;
    public const int Login = 100;
    /// <summary>Un mot de passe plus long ne sert à rien (BCrypt ne lit que 72 octets) et coûte du calcul au hachage.</summary>
    public const int Password = 256;
    public const int Email = 254;
    public const int Matricule = 50;
    public const int Server = 256;
    /// <summary>Longueur maximale d'un nom SQL Server (sysname) : base, compte SQL.</summary>
    public const int SqlName = 128;
    public const int RefreshToken = 200;

    public static readonly string[] DatabaseTypes = { "STANDARD", "AUTRE" };

    public static bool HasNoControlCharacter(string? value) => value is null || !value.Any(char.IsControl);

    public static bool IsKnownDatabaseType(string? value)
        => value is null || DatabaseTypes.Contains(value, StringComparer.OrdinalIgnoreCase);
}
