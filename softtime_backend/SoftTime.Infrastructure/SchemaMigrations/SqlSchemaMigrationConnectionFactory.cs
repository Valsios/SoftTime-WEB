using Microsoft.Data.SqlClient;

namespace SoftTime.Infrastructure.SchemaMigrations;

/// <summary>Ouvre une connexion SQL Server dédiée à la migration (chaîne ConnectionStrings:SoftTime).</summary>
public sealed class SqlSchemaMigrationConnectionFactory : ISchemaMigrationConnectionFactory
{
    private readonly string _connectionString;
    private readonly int _commandTimeoutSeconds;

    public SqlSchemaMigrationConnectionFactory(string connectionString, int commandTimeoutSeconds = 600)
    {
        _connectionString = connectionString;
        _commandTimeoutSeconds = commandTimeoutSeconds;
    }

    public async Task<ISchemaMigrationConnection> OpenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            throw new SchemaMigrationException("Migration du schéma impossible : la chaîne de connexion « SoftTime » n'est pas configurée.");
        var connection = new SqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(ct);
        }
        catch (SqlException ex)
        {
            await connection.DisposeAsync();
            // Le message SQL d'une ouverture de connexion peut citer serveur ou compte : seul le numéro est repris.
            throw new SchemaMigrationException(
                $"Migration du schéma impossible : connexion à la base SoftTime refusée (erreur SQL {ex.Number}).", ex);
        }
        var session = new SqlSchemaMigrationConnection(connection, _commandTimeoutSeconds);
        await session.ExecuteBatchAsync("SET XACT_ABORT ON;", ct);
        return session;
    }
}
