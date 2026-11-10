namespace SoftTime.Api.Security;

/// <summary>Réglages du jeton d'accès (section « Jwt »). La clé ne vient jamais d'appsettings.json.</summary>
public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public const int MinimumKeyBytes = 32;

    /// <summary>Ancienne clé de développement publiée dans le dépôt : refusée, quiconque la connaît peut forger un jeton.</summary>
    public const string PublishedDevelopmentKey = "SoftTime_Dev_ChangeMe_32chars_minimum!!";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 30;
}
