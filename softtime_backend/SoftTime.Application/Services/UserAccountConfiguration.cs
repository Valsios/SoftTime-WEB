using Microsoft.AspNetCore.Identity;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Application.Services;

/// <summary>
/// Résultat détaillé d'une opération de migration des mots de passe.
/// Trace les comptes migrés, déjà sécurisés, vides ou nécessitant une attention administrative.
/// </summary>
public sealed record PasswordMigrationResult
{
    public int TotalScanned { get; init; }
    public int MigratedCount { get; init; }
    public int AlreadyHashedV3Count { get; init; }
    public int PreservedV2Count { get; init; }
    public int SkippedEmptyCount { get; init; }
    public int AmbiguousCount { get; init; }
    public List<AmbiguousAccountEntry> AmbiguousAccounts { get; init; } = new();
}

/// <summary>
/// Information sur un compte dont la valeur de mot de passe est ambiguë, de format externe ou mal formée.
/// Ces comptes sont scrupuleusement préservés sans modification pour empêcher toute corruption irréversible.
/// </summary>
public sealed record AmbiguousAccountEntry(
    decimal UserId,
    string? Login,
    PasswordClassification Classification,
    string Diagnostic);

/// <summary>
/// Stockage requis par les comptes utilisateurs et migration de sécurité.
/// Les modifications DDL de schéma structurelles (ex: élargissement de colonne) sont isolées
/// dans des scripts DBA explicites (voir migrations/upgrade_password_column_schema.sql)
/// et ne sont JAMAIS exécutées à chaud au runtime applicatif pour prévenir tout risque
/// de verrouillage exclusif (Sch-M), perte de contrainte NOT NULL ou violation de privilèges.
/// </summary>
public static class UserAccountConfiguration
{
    /// <summary>
    /// Initialisation minimale du stockage au démarrage de l'API :
    /// ajoute la colonne EMAIL si absente (requis pour l'envoi d'identifiants).
    /// </summary>
    public static Task EnsureStorageAsync(IUnitOfWork uow, CancellationToken ct = default) => uow.ExecuteSqlAsync("""
        IF COL_LENGTH(N'dbo.T_RESPONSABLE', N'EMAIL') IS NULL
            ALTER TABLE dbo.T_RESPONSABLE ADD EMAIL nvarchar(200) NULL;
        """, ct);

    /// <summary>
    /// Migration fine et idempotente des mots de passe historiques avec rapport détaillé.
    /// Traite UNIQUEMENT les mots de passe avérés en clair (LegacyPlainText).
    /// Préserve scrupuleusement les hachages Identity V2/V3 ainsi que les formats externes
    /// (MD5, BCrypt, etc.) et valeurs corrompues sans jamais les écraser ou les corrompre.
    /// </summary>
    public static async Task<PasswordMigrationResult> MigrateLegacyPasswordsDetailedAsync(
        IUnitOfWork uow,
        IPasswordHasher<T_RESPONSABLE>? hasher = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        hasher ??= new PasswordHasher<T_RESPONSABLE>();
        var repo = uow.Repository<T_RESPONSABLE>();
        var users = await repo.ListAsync(_ => true, ct);

        var result = new PasswordMigrationResult { TotalScanned = users.Count };
        var migrated = 0;
        var v3Count = 0;
        var v2Count = 0;
        var emptyCount = 0;
        var ambiguousCount = 0;

        foreach (var user in users)
        {
            ct.ThrowIfCancellationRequested();
            var classification = PasswordSecurityHelper.Classify(user.PASSWORD);

            switch (classification)
            {
                case PasswordClassification.NullOrEmpty:
                    emptyCount++;
                    break;

                case PasswordClassification.IdentityV3:
                    v3Count++;
                    break;

                case PasswordClassification.IdentityV2:
                    // Préservé intact : sera re-haché en V3 de façon transparente à la prochaine connexion réussie
                    v2Count++;
                    break;

                case PasswordClassification.LegacyPlainText:
                    // Mot de passe en clair avéré : sécurisation immédiate en PBKDF2 HMAC-SHA256 (100k itérations)
                    user.PASSWORD = hasher.HashPassword(user, user.PASSWORD!);
                    repo.Update(user);
                    migrated++;
                    break;

                case PasswordClassification.UnrecognizedHash:
                    ambiguousCount++;
                    result.AmbiguousAccounts.Add(new AmbiguousAccountEntry(
                        user.IDRESPONSABLE,
                        user.LOGIN,
                        classification,
                        "Format de hachage externe (MD5, SHA, BCrypt, etc.). Valeur préservée intacte. Réinitialisation administrative requise."));
                    break;

                case PasswordClassification.AmbiguousOrMalformed:
                    ambiguousCount++;
                    result.AmbiguousAccounts.Add(new AmbiguousAccountEntry(
                        user.IDRESPONSABLE,
                        user.LOGIN,
                        classification,
                        "Valeur mal formée ou tronquée. Valeur préservée sans altération pour traitement contrôlé."));
                    break;
            }
        }

        if (migrated > 0)
        {
            await uow.SaveChangesAsync(ct);
        }

        return result with
        {
            MigratedCount = migrated,
            AlreadyHashedV3Count = v3Count,
            PreservedV2Count = v2Count,
            SkippedEmptyCount = emptyCount,
            AmbiguousCount = ambiguousCount
        };
    }

    /// <summary>
    /// Migration idempotente et contrôlée des mots de passe historiques stockés en clair.
    /// Retourne le nombre exact de comptes en clair sécurisés vers Identity V3.
    /// </summary>
    public static async Task<int> MigrateLegacyPasswordsAsync(
        IUnitOfWork uow,
        IPasswordHasher<T_RESPONSABLE>? hasher = null,
        CancellationToken ct = default)
    {
        var result = await MigrateLegacyPasswordsDetailedAsync(uow, hasher, ct);
        return result.MigratedCount;
    }
}
