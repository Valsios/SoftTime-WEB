using Microsoft.Extensions.Configuration;

namespace SoftTime.Infrastructure.SchemaMigrations;

/// <summary>Options « Database:AutoMigrate » et « Database:AutoMigrateLockTimeoutSeconds ».</summary>
public sealed record SchemaMigrationOptions
{
    public const string Section = "Database";

    public const int DefaultLockTimeoutSeconds = 60;

    /// <summary>Exécute les scripts versionnés au démarrage. Désactivé par défaut.</summary>
    public bool AutoMigrate { get; set; }

    /// <summary>Attente maximale du verrou applicatif (deux démarrages simultanés). Une valeur négative vaut le défaut.</summary>
    [ConfigurationKeyName("AutoMigrateLockTimeoutSeconds")]
    public int LockTimeoutSeconds { get; set; } = DefaultLockTimeoutSeconds;

    /// <summary>Remplace un délai invalide par la valeur par défaut, comme le faisait la lecture historique.</summary>
    public void Normalize()
    {
        if (LockTimeoutSeconds < 0)
            LockTimeoutSeconds = DefaultLockTimeoutSeconds;
    }
}
