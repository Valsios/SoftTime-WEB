using Microsoft.AspNetCore.Identity;

using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Application.Services;

/// <summary>
/// Stockage requis par les comptes utilisateurs et migration de sécurité.
/// Les modifications DDL de schéma structurelles (ex: élargissement de colonne) sont isolées
/// dans des scripts DBA explicites (voir migrations/upgrade_password_column_schema.sql)
/// et ne sont JAMAIS exécutées à chaud au runtime applicatif pour prévenir tout risque
/// de verrouillage exclusif (Sch-M), perte de contrainte NOT NULL ou violation de privilèges.
/// </summary>
public static class UserAccountConfiguration
{
    /// <summary>Schéma créé s'il est absent (scripts idempotents).</summary>
    private const string StorageSql = """
        IF COL_LENGTH(N'dbo.T_RESPONSABLE', N'EMAIL') IS NULL
            ALTER TABLE dbo.T_RESPONSABLE ADD EMAIL nvarchar(200) NULL;
        """;

    /// <summary>
    /// Initialisation minimale du stockage au démarrage de l'API :
    /// ajoute la colonne EMAIL si absente (requis pour l'envoi d'identifiants).
    /// </summary>
    public static Task EnsureStorageAsync(IUnitOfWork uow, CancellationToken ct = default)
        => SchemaPresenceGuard.ExecuteAsync(uow, "T_RESPONSABLE", StorageSql, ct);

    /// <summary>
    /// Migration fine et idempotente des mots de passe historiques avec rapport détaillé.
    /// Traite UNIQUEMENT les mots de passe avérés en clair (LegacyPlainText).
    /// Préserve scrupuleusement les hachages Identity V2/V3 ainsi que les formats externes
    /// (MD5, BCrypt, etc.) et valeurs corrompues sans jamais les écraser ou les corrompre.
    /// </summary>
    public static async Task<PasswordMigrationResult> MigrateLegacyPasswordsDetailedAsync(
        IUnitOfWork uow,
        IPasswordHasher<Responsable>? hasher = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        hasher ??= new PasswordHasher<Responsable>();
        var repo = uow.Repository<Responsable>();
        var users = await repo.ListAsync(_ => true, ct);
        var classes = Classer(users, ct);

        // Seuls les mots de passe en clair avérés sont sécurisés (PBKDF2 HMAC-SHA256, 100k itérations).
        // Les hachages V2 sont préservés intacts : re-hachés en V3 à la prochaine connexion réussie.
        var enClair = await SecuriserAsync(uow, hasher, repo, classes, ct);

        var ambigus = classes
            .Where(c => AmbiguityReason(c.Classe) is not null)
            .Select(c => new AmbiguousAccountEntry(c.User.IdResponsable, c.User.Login, c.Classe, AmbiguityReason(c.Classe)!))
            .ToList();
        var result = new PasswordMigrationResult { TotalScanned = users.Count };
        result.AmbiguousAccounts.AddRange(ambigus);
        return result with
        {
            MigratedCount = enClair.Count,
            AlreadyHashedV3Count = classes.Count(c => c.Classe == PasswordClassification.IdentityV3),
            PreservedV2Count = classes.Count(c => c.Classe == PasswordClassification.IdentityV2),
            SkippedEmptyCount = classes.Count(c => c.Classe == PasswordClassification.NullOrEmpty),
            AmbiguousCount = ambigus.Count
        };
    }

    /// <summary>Classification du mot de passe stocké de chaque compte.</summary>
    private static List<(Responsable User, PasswordClassification Classe)> Classer(
        List<Responsable> users, CancellationToken ct)
    {
        var classes = users
            .Select(user =>
            {
                ct.ThrowIfCancellationRequested();
                return (User: user, Classe: PasswordHashClassifier.Classify(user.Password));
            })
            .ToList();
        return classes;
    }

    /// <summary>Hache les mots de passe en clair avérés et retourne les comptes sécurisés.</summary>
    private static async Task<List<Responsable>> SecuriserAsync(
        IUnitOfWork uow, IPasswordHasher<Responsable> hasher, IRepository<Responsable> repo, List<(Responsable User, PasswordClassification Classe)> classes, CancellationToken ct)
    {
        var enClair = classes.Where(c => c.Classe == PasswordClassification.LegacyPlainText).Select(c => c.User).ToList();
        foreach (var user in enClair)
        {
            user.Password = hasher.HashPassword(user, user.Password!);
            repo.Update(user);
        }
        if (enClair.Count > 0)
            await uow.SaveChangesAsync(ct);
        return enClair;
    }

    /// <summary>Raison de conservation d'une valeur non migrable, ou <c>null</c> si la valeur n'est pas ambiguë.</summary>
    private static string? AmbiguityReason(PasswordClassification classe) => classe switch
    {
        PasswordClassification.UnrecognizedHash =>
            "Format de hachage externe (MD5, SHA, BCrypt, etc.). Valeur préservée intacte. Réinitialisation administrative requise.",
        PasswordClassification.AmbiguousOrMalformed =>
            "Valeur mal formée ou tronquée. Valeur préservée sans altération pour traitement contrôlé.",
        _ => null
    };

    /// <summary>
    /// Migration idempotente et contrôlée des mots de passe historiques stockés en clair.
    /// Retourne le nombre exact de comptes en clair sécurisés vers Identity V3.
    /// </summary>
    public static async Task<int> MigrateLegacyPasswordsAsync(
        IUnitOfWork uow,
        IPasswordHasher<Responsable>? hasher = null,
        CancellationToken ct = default)
    {
        var result = await MigrateLegacyPasswordsDetailedAsync(uow, hasher, ct);
        return result.MigratedCount;
    }
}
