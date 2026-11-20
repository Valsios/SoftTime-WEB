using System.Data;

using Microsoft.Data.SqlClient;

namespace SoftTime.Infrastructure.Integrations;

/// <summary>Colonne de la table d'écriture (INFORMATION_SCHEMA.COLUMNS).</summary>
internal sealed record OtherPayrollColumn(string DataType, byte? Precision, int? Scale);

/// <summary>
/// Accès SQL de l'écriture de paie AUTRE : lecture du schéma, comptage par matricule et mises à jour dans une
/// transaction. Isolé pour que les tests vérifient le SQL exact et l'atomicité sans base réelle.
/// </summary>
internal interface IOtherPayrollDatabase : IAsyncDisposable
{
    Task<IReadOnlyDictionary<string, OtherPayrollColumn>> ReadColumnsAsync(string schema, string table, CancellationToken ct);

    Task<long> CountAsync(string sql, string matricule, CancellationToken ct);

    Task BeginTransactionAsync(CancellationToken ct);

    Task<int> ExecuteUpdateAsync(string sql, decimal value, string matricule, CancellationToken ct);

    Task CommitAsync(CancellationToken ct);

    Task RollbackAsync(CancellationToken ct);
}

/// <summary>Implémentation SQL Server : mêmes commandes et mêmes paramètres que l'écriture historique.</summary>
internal sealed class SqlOtherPayrollDatabase : IOtherPayrollDatabase
{
    private readonly string _connectionString;
    private SqlConnection? _connection;
    private SqlTransaction? _transaction;

    public SqlOtherPayrollDatabase(string connectionString) => _connectionString = connectionString;

    public async Task<IReadOnlyDictionary<string, OtherPayrollColumn>> ReadColumnsAsync(string schema, string table, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            SELECT COLUMN_NAME, DATA_TYPE, NUMERIC_PRECISION, NUMERIC_SCALE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
            """, await ConnectionAsync(ct));
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", table);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new Dictionary<string, OtherPayrollColumn>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(ct))
            result[reader.GetString(0)] = new OtherPayrollColumn(reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetByte(2), reader.IsDBNull(3) ? null : reader.GetInt32(3));
        return result;
    }

    public async Task<long> CountAsync(string sql, string matricule, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, await ConnectionAsync(ct));
        cmd.Parameters.Add("@matricule", SqlDbType.NVarChar, 256).Value = matricule;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task BeginTransactionAsync(CancellationToken ct) =>
        _transaction = (SqlTransaction)await (await ConnectionAsync(ct)).BeginTransactionAsync(ct);

    public async Task<int> ExecuteUpdateAsync(string sql, decimal value, string matricule, CancellationToken ct)
    {
        var transaction = _transaction ?? throw new InvalidOperationException("Aucune transaction d'écriture de paie ouverte.");
        await using var cmd = new SqlCommand(sql, transaction.Connection, transaction);
        cmd.Parameters.Add("@value", SqlDbType.Decimal).Value = value;
        cmd.Parameters["@value"].Precision = 18;
        cmd.Parameters["@value"].Scale = 4;
        cmd.Parameters.Add("@matricule", SqlDbType.NVarChar, 256).Value = matricule;
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public Task CommitAsync(CancellationToken ct) =>
        (_transaction ?? throw new InvalidOperationException("Aucune transaction d'écriture de paie ouverte.")).CommitAsync(ct);

    public Task RollbackAsync(CancellationToken ct) => _transaction?.RollbackAsync(ct) ?? Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
            await _transaction.DisposeAsync();
        if (_connection is not null)
            await _connection.DisposeAsync();
    }

    private async Task<SqlConnection> ConnectionAsync(CancellationToken ct)
    {
        if (_connection is null)
        {
            _connection = new SqlConnection(_connectionString);
            await _connection.OpenAsync(ct);
        }
        return _connection;
    }
}
