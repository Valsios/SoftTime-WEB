using System.Data;
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

public sealed class SqlSchemaMigrationConnection : ISchemaMigrationConnection
{
    private readonly SqlConnection _connection;
    private readonly int _commandTimeoutSeconds;
    private SqlTransaction? _transaction;

    public SqlSchemaMigrationConnection(SqlConnection connection, int commandTimeoutSeconds)
    {
        _connection = connection;
        _commandTimeoutSeconds = commandTimeoutSeconds;
    }

    public async Task<bool> TryAcquireLockAsync(int timeoutSeconds, CancellationToken ct)
    {
        await using var command = Command("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = @resource, @LockMode = N'Exclusive',
                                             @LockOwner = N'Session', @LockTimeout = @timeout;
            SELECT @result;
            """);
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = SchemaMigrator.LockResource });
        command.Parameters.Add(new SqlParameter("@timeout", SqlDbType.Int) { Value = checked(timeoutSeconds * 1000) });
        command.CommandTimeout = Math.Max(_commandTimeoutSeconds, timeoutSeconds + 30);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        return result >= 0; // 0 : obtenu, 1 : obtenu après attente ; < 0 : délai dépassé, annulé, interblocage.
    }

    public async Task ReleaseLockAsync(CancellationToken ct)
    {
        await using var command = Command("""
            IF APPLOCK_MODE(N'public', @resource, N'Session') <> N'NoLock'
                EXEC sys.sp_releaseapplock @Resource = @resource, @LockOwner = N'Session';
            """);
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = SchemaMigrator.LockResource });
        await command.ExecuteNonQueryAsync(ct);
    }

    public Task EnsureVersionTableAsync(CancellationToken ct) => ExecuteBatchAsync("""
        IF OBJECT_ID(N'dbo.T_SCHEMA_VERSION', N'U') IS NULL
        CREATE TABLE dbo.T_SCHEMA_VERSION (
            VERSION          int           NOT NULL,
            NOM              nvarchar(260) NOT NULL,
            SOMME_CONTROLE   char(64)      NOT NULL,
            DATE_APPLICATION datetime2     NOT NULL CONSTRAINT DF_T_SCHEMA_VERSION_DATE DEFAULT SYSUTCDATETIME(),
            APPLIQUE_PAR     nvarchar(256) NOT NULL,
            CONSTRAINT PK_T_SCHEMA_VERSION PRIMARY KEY (VERSION)
        );
        """, ct);

    public async Task<IReadOnlyList<AppliedMigration>> GetAppliedAsync(CancellationToken ct)
    {
        await using var command = Command("SELECT VERSION, NOM, SOMME_CONTROLE FROM dbo.T_SCHEMA_VERSION ORDER BY VERSION;");
        await using var reader = await command.ExecuteReaderAsync(ct);
        var applied = new List<AppliedMigration>();
        while (await reader.ReadAsync(ct))
            applied.Add(new AppliedMigration(reader.GetInt32(0), reader.GetString(1), reader.GetString(2).Trim()));
        return applied;
    }

    public async Task BeginTransactionAsync(CancellationToken ct)
        => _transaction = (SqlTransaction)await _connection.BeginTransactionAsync(ct);

    public async Task ExecuteBatchAsync(string sql, CancellationToken ct)
    {
        await using var command = Command(sql);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task RecordAppliedAsync(MigrationScript script, string appliedBy, CancellationToken ct)
    {
        await using var command = Command("""
            INSERT INTO dbo.T_SCHEMA_VERSION (VERSION, NOM, SOMME_CONTROLE, APPLIQUE_PAR)
            VALUES (@version, @nom, @somme, @par);
            """);
        command.Parameters.Add(new SqlParameter("@version", SqlDbType.Int) { Value = script.Version });
        command.Parameters.Add(new SqlParameter("@nom", SqlDbType.NVarChar, 260) { Value = script.Name });
        command.Parameters.Add(new SqlParameter("@somme", SqlDbType.Char, 64) { Value = script.Checksum });
        command.Parameters.Add(new SqlParameter("@par", SqlDbType.NVarChar, 256) { Value = appliedBy });
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task CommitAsync(CancellationToken ct)
    {
        if (_transaction == null)
            return;
        await _transaction.CommitAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackAsync(CancellationToken ct)
    {
        if (_transaction == null)
            return;
        try
        {
            await _transaction.RollbackAsync(ct);
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction != null)
            await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private SqlCommand Command(string sql)
        => new(sql, _connection, _transaction) { CommandTimeout = _commandTimeoutSeconds };
}
