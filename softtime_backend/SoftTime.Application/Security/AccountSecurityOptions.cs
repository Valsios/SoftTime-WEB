using Microsoft.Extensions.Options;

namespace SoftTime.Application.Security;

/// <summary>Réglages de sécurité des comptes (section « Security ») : session, verrouillage, limitation des requêtes.</summary>
public sealed class AccountSecurityOptions
{
    public const string Section = "Security";

    /// <summary>Durée de vie d'un jeton de rafraîchissement, renouvelé (rotation) à chaque utilisation.</summary>
    public int RefreshTokenDays { get; set; } = 7;

    /// <summary>Échecs de connexion consécutifs avant verrouillage du compte.</summary>
    public int MaxFailedLoginAttempts { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;

    /// <summary>Requêtes par minute et par adresse IP sur la connexion, le rafraîchissement et la création initiale.</summary>
    public int AuthRequestsPerMinute { get; set; } = 20;
}

public sealed class AccountSecurityOptionsValidator : IValidateOptions<AccountSecurityOptions>
{
    public ValidateOptionsResult Validate(string? name, AccountSecurityOptions options)
    {
        var errors = new List<string>();
        if (options.RefreshTokenDays is < 1 or > 30)
            errors.Add("Security:RefreshTokenDays doit être compris entre 1 et 30.");
        if (options.MaxFailedLoginAttempts is < 1 or > 20)
            errors.Add("Security:MaxFailedLoginAttempts doit être compris entre 1 et 20.");
        if (options.LockoutMinutes is < 1 or > 1440)
            errors.Add("Security:LockoutMinutes doit être compris entre 1 et 1440.");
        if (options.AuthRequestsPerMinute < 1)
            errors.Add("Security:AuthRequestsPerMinute doit être positif.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
