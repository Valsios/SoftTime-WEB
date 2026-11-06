using System.Globalization;

using Microsoft.Extensions.Configuration;

namespace SoftTime.Infrastructure.SchemaMigrations;

/// <summary>Options « Database:AutoMigrate » et « Database:AutoMigrateLockTimeoutSeconds ».</summary>
public sealed record SchemaMigrationOptions
{
    public const int DefaultLockTimeoutSeconds = 60;

    /// <summary>Exécute les scripts versionnés au démarrage. Désactivé par défaut.</summary>
    public bool AutoMigrate { get; init; }

    /// <summary>Attente maximale du verrou applicatif (deux démarrages simultanés).</summary>
    public int LockTimeoutSeconds { get; init; } = DefaultLockTimeoutSeconds;

    public static SchemaMigrationOptions FromConfiguration(IConfiguration configuration)
    {
        var autoMigrate = bool.TryParse(configuration["Database:AutoMigrate"], out var enabled) && enabled;
        var timeout = int.TryParse(configuration["Database:AutoMigrateLockTimeoutSeconds"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0
            ? seconds
            : DefaultLockTimeoutSeconds;
        return new SchemaMigrationOptions { AutoMigrate = autoMigrate, LockTimeoutSeconds = timeout };
    }
}
