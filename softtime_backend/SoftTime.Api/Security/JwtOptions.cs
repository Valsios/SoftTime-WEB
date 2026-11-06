using System.Text;

using Microsoft.Extensions.Options;

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

/// <summary>Refuse le démarrage sans clé de signature sûre, plutôt que de signer avec une valeur connue.</summary>
public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public const string MissingKeyMessage =
        "Clé de signature JWT absente : définissez Jwt:Key (au moins 32 caractères) avec "
        + "« dotnet user-secrets set \"Jwt:Key\" \"<valeur>\" » en développement, ou la variable d'environnement Jwt__Key.";

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Key) || Encoding.UTF8.GetByteCount(options.Key) < JwtOptions.MinimumKeyBytes)
            errors.Add(MissingKeyMessage);
        else if (options.Key == JwtOptions.PublishedDevelopmentKey)
            errors.Add("La clé JWT de développement publiée dans le dépôt est refusée : générez une nouvelle clé.");
        if (string.IsNullOrWhiteSpace(options.Issuer) || string.IsNullOrWhiteSpace(options.Audience))
            errors.Add("Jwt:Issuer et Jwt:Audience sont obligatoires.");
        if (options.AccessTokenMinutes is < 1 or > 60)
            errors.Add("Jwt:AccessTokenMinutes doit être compris entre 1 et 60 (convention : 15 à 30).");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
