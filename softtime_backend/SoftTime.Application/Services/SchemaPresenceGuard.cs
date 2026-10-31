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
            throw new InvalidOperationException(MissingSchemaMessage);
        }
    }

    public static string GuardedSql(string table, string sql) => $"""
        IF OBJECT_ID(N'dbo.{table}', N'U') IS NULL
            THROW 51990, N'{Marker}: dbo.{table}', 1;

        {sql}
        """;
}
