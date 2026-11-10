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

    /// <summary>
    /// Migre en lot les mots de passe en clair au démarrage. Faux par défaut : chaque mot de passe est migré
    /// à la connexion de son utilisateur, sans verrou concurrent entre instances.
    /// </summary>
    public bool MigrateLegacyPasswordsOnStartup { get; set; }
}
