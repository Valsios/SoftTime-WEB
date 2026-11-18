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

    public const string MissingPayrollVariablesMessage =
        "Les variables envoyées à la paie sont absentes (T_PAIE_VARIABLE, T_PAIE_ENVOI) : activez Database:AutoMigrate "
        + "(développement) ou exécutez migrations/versions/007_variables_paie.sql.";

    /// <summary>
    /// Sans la migration 007, la synchronisation de paie ne pourrait ni lire la configuration des variables ni poser
    /// le marqueur d'envoi : le démarrage s'arrête à la place avec un message qui indique la marche à suivre.
    /// </summary>
    public static async Task EnsurePayrollVariablesAsync(IUnitOfWork uow, CancellationToken ct)
    {
        try
        {
            await uow.ExecuteSqlAsync(GuardedSql("T_PAIE_VARIABLE", $"""
                IF OBJECT_ID(N'dbo.T_PAIE_ENVOI', N'U') IS NULL
                    THROW 51990, N'{Marker}: dbo.T_PAIE_ENVOI', 1;
                """), ct);
        }
        catch (Exception ex) when (ex.Message.Contains(Marker, StringComparison.Ordinal))
        {
            throw new DomainException(MissingPayrollVariablesMessage);
        }
    }

    public const string MissingPayrollOptionsMessage =
        "Les options de paie sont absentes (T_PAIE_OPTION) : activez Database:AutoMigrate "
        + "(développement) ou exécutez migrations/versions/008_options_paie.sql.";

    /// <summary>
    /// Sans la migration 008, la lecture des options de paie (remise à 0 des salariés sans valeur) échouerait sur une erreur SQL
    /// obscure à chaque envoi : le démarrage s'arrête à la place avec un message qui indique la marche à suivre.
    /// </summary>
    public static async Task EnsurePayrollOptionsAsync(IUnitOfWork uow, CancellationToken ct)
    {
        try
        {
            await uow.ExecuteSqlAsync(GuardedSql("T_PAIE_OPTION", "SELECT 1;"), ct);
        }
        catch (Exception ex) when (ex.Message.Contains(Marker, StringComparison.Ordinal))
        {
            throw new DomainException(MissingPayrollOptionsMessage);
        }
    }

    public const string MissingPayrollSnapshotsMessage =
        "Les instantanés des envois en paie sont absents (T_PAIE_INSTANTANE, T_PAIE_INSTANTANE_LIGNE) : activez Database:AutoMigrate "
        + "(développement) ou exécutez migrations/versions/009_instantanes_paie.sql.";

    /// <summary>
    /// Sans la migration 009, chaque envoi en paie échouerait à figer son instantané (AUTRE : envoi annulé ; STANDARD : envoi
    /// INCERTAIN) : le démarrage s'arrête à la place avec un message qui indique la marche à suivre.
    /// </summary>
    public static async Task EnsurePayrollSnapshotsAsync(IUnitOfWork uow, CancellationToken ct)
    {
        try
        {
            await uow.ExecuteSqlAsync(GuardedSql("T_PAIE_INSTANTANE", $"""
                IF OBJECT_ID(N'dbo.T_PAIE_INSTANTANE_LIGNE', N'U') IS NULL
                    THROW 51990, N'{Marker}: dbo.T_PAIE_INSTANTANE_LIGNE', 1;
                """), ct);
        }
        catch (Exception ex) when (ex.Message.Contains(Marker, StringComparison.Ordinal))
        {
            throw new DomainException(MissingPayrollSnapshotsMessage);
        }
    }

    public const string MissingPayrollClosuresMessage =
        "La clôture de paie est absente (T_PAIE_CLOTURE) : activez Database:AutoMigrate (développement) "
        + "ou exécutez migrations/versions/010_cloture_paie.sql.";

    /// <summary>
    /// Sans la migration 010, chaque contrôle « période clôturée » (envoi, pointages, heures corrigées…) échouerait sur une
    /// erreur SQL obscure : le démarrage s'arrête à la place avec un message qui indique la marche à suivre.
    /// </summary>
    public static async Task EnsurePayrollClosuresAsync(IUnitOfWork uow, CancellationToken ct)
    {
        try
        {
            await uow.ExecuteSqlAsync(GuardedSql("T_PAIE_CLOTURE", "SELECT 1;"), ct);
        }
        catch (Exception ex) when (ex.Message.Contains(Marker, StringComparison.Ordinal))
        {
            throw new DomainException(MissingPayrollClosuresMessage);
        }
    }

    public const string MissingPayrollDomainsMessage =
        "Les domaines des envois en paie sont absents (T_PAIE_ENVOI.DOMAINE, T_PAIE_INSTANTANE.DOMAINE, T_PAIE_CLOTURE_DOMAINE) : "
        + "activez Database:AutoMigrate (développement) ou exécutez migrations/versions/011_domaines_paie.sql.";

    /// <summary>
    /// Sans la migration 011, chaque lecture ou écriture d'un envoi en paie échouerait sur une colonne DOMAINE inconnue
    /// (envoi des heures compris) : le démarrage s'arrête à la place avec un message qui indique la marche à suivre.
    /// </summary>
    public static async Task EnsurePayrollDomainsAsync(IUnitOfWork uow, CancellationToken ct)
    {
        try
        {
            await uow.ExecuteSqlAsync(GuardedSql("T_PAIE_CLOTURE_DOMAINE", $"""
                IF COL_LENGTH(N'dbo.T_PAIE_ENVOI', N'DOMAINE') IS NULL
                    THROW 51990, N'{Marker}: dbo.T_PAIE_ENVOI.DOMAINE', 1;
                IF COL_LENGTH(N'dbo.T_PAIE_INSTANTANE', N'DOMAINE') IS NULL
                    THROW 51990, N'{Marker}: dbo.T_PAIE_INSTANTANE.DOMAINE', 1;
                """), ct);
        }
        catch (Exception ex) when (ex.Message.Contains(Marker, StringComparison.Ordinal))
        {
            throw new DomainException(MissingPayrollDomainsMessage);
        }
    }

    public static string GuardedSql(string table, string sql) => $"""
        IF OBJECT_ID(N'dbo.{table}', N'U') IS NULL
            THROW 51990, N'{Marker}: dbo.{table}', 1;

        {sql}
        """;
}
