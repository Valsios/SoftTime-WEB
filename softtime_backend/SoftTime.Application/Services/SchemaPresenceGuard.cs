using SoftTime.Domain.Repositories;

namespace SoftTime.Application.Services;

/// <summary>
/// Garde des méthodes Ensure* du démarrage : sur une base sans schéma SoftTime, elles échoueraient
/// sur une erreur SQL obscure (objet introuvable). La garde vérifie la table cible dans le même
/// lot SQL et lève à la place un message qui indique comment créer le schéma.
/// </summary>
public static class SchemaPresenceGuard
{
    public const string MissingSchemaMessage =
        "Le schéma de la base est absent : activez Database:AutoMigrate (développement) ou exécutez migrations/versions/001_schema_initial.sql.";

    // Marqueur levé par le THROW SQL : seul ce lot peut produire une erreur qui le contient.
    internal const string Marker = "SOFTTIME_SCHEMA_ABSENT";

    /// <summary>Exécute <paramref name="sql"/> précédé d'un contrôle d'existence de dbo.<paramref name="table"/>.</summary>
    public static async Task ExecuteAsync(IUnitOfWork uow, string table, string sql, CancellationToken ct)
    {
        try
        {
            await uow.ExecuteSqlAsync(GuardedSql(table, sql), ct);
        }
        catch (Exception ex) when (ex.Message.Contains(Marker, StringComparison.Ordinal))
        {
            throw new DomainException(MissingSchemaMessage);
        }
    }

    public const string MissingAuditJournalMessage =
        "Le journal d'audit est absent : activez Database:AutoMigrate (développement) ou exécutez migrations/versions/003_journal_central.sql.";

    /// <summary>
    /// Sans suivi_JOURNAL, chaque sauvegarde d'une entité auditée échouerait sur une erreur SQL
    /// « Invalid object name » ; le démarrage s'arrête à la place avec un message qui indique la marche à suivre.
    /// </summary>
    public static async Task EnsureAuditJournalAsync(IUnitOfWork uow, CancellationToken ct)
    {
        try
        {
            await uow.ExecuteSqlAsync(GuardedSql("suivi_JOURNAL", "SELECT 1;"), ct);
        }
        catch (Exception ex) when (ex.Message.Contains(Marker, StringComparison.Ordinal))
        {
            throw new DomainException(MissingAuditJournalMessage);
        }
    }

    public const string MissingAccountSecurityMessage =
        "La sécurité des comptes est absente (jetons de rafraîchissement, verrouillage) : activez Database:AutoMigrate (développement) "
        + "ou exécutez migrations/versions/004_securite_comptes.sql.";

    /// <summary>Sans la migration 004, la lecture de T_RESPONSABLE échouerait sur une colonne inconnue : connexion impossible.</summary>
    public static async Task EnsureAccountSecurityAsync(IUnitOfWork uow, CancellationToken ct)
    {
        try
        {
            await uow.ExecuteSqlAsync(GuardedSql("T_REFRESH_TOKEN", $"""
                IF COL_LENGTH(N'dbo.T_RESPONSABLE', N'TENTATIVES_ECHOUEES') IS NULL OR COL_LENGTH(N'dbo.T_RESPONSABLE', N'VERROUILLE_JUSQUA') IS NULL
                    THROW 51990, N'{Marker}: dbo.T_RESPONSABLE', 1;
                """), ct);
        }
        catch (Exception ex) when (ex.Message.Contains(Marker, StringComparison.Ordinal))
        {
            throw new DomainException(MissingAccountSecurityMessage);
        }
    }

    public const string MissingUserPermissionDeviationsMessage =
        "Le stockage des droits en écarts est absent : activez Database:AutoMigrate (développement) "
        + "ou exécutez migrations/versions/006_ecarts_permissions_utilisateur.sql.";

    /// <summary>
    /// Sans la migration 006, les droits des comptes seraient lus avec le mauvais format (copie lue comme écarts) :
    /// le démarrage s'arrête à la place avec un message qui indique la marche à suivre.
    /// </summary>
    public static async Task EnsureUserPermissionDeviationsAsync(IUnitOfWork uow, CancellationToken ct)
    {
        try
        {
            await uow.ExecuteSqlAsync(GuardedSql("T_USER_PERMISSION", $"""
                IF COL_LENGTH(N'dbo.T_USER_PERMISSION', N'IS_DEVIATION') IS NULL
                    THROW 51990, N'{Marker}: dbo.T_USER_PERMISSION', 1;
                """), ct);
        }
        catch (Exception ex) when (ex.Message.Contains(Marker, StringComparison.Ordinal))
        {
            throw new DomainException(MissingUserPermissionDeviationsMessage);
        }
    }

    public static string GuardedSql(string table, string sql) => $"""
        IF OBJECT_ID(N'dbo.{table}', N'U') IS NULL
            THROW 51990, N'{Marker}: dbo.{table}', 1;

        {sql}
        """;
}
