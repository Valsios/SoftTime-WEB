using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

using SoftTime.Application.Abstractions.DataSources;
using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Services;

namespace SoftTime.Infrastructure.Integrations;

/// <summary>
/// Schéma (dbo) de la base de paie active, lu dans INFORMATION_SCHEMA avec la connexion configurée de la base. Lecture
/// seule ; une erreur SQL devient une <see cref="ExternalConnectionException"/> au message lisible.
/// </summary>
public sealed class PayrollTargetSchema : IPayrollTargetSchema
{
    private readonly ITenantConnectionService _tenant;
    private readonly ILogger<PayrollTargetSchema> _logger;

    public PayrollTargetSchema(ITenantConnectionService tenant, ILogger<PayrollTargetSchema> logger)
    {
        _tenant = tenant;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> ListTablesAsync(CancellationToken ct = default)
    {
        var tables = new List<string>();
        await LireAsync(
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_SCHEMA = 'dbo' ORDER BY TABLE_NAME",
            null, reader => tables.Add(reader.GetString(0)), ct);
        return tables;
    }

    public async Task<IReadOnlyList<PayrollTargetColumn>> ListColumnsAsync(string table, CancellationToken ct = default)
    {
        var colonnes = new List<PayrollTargetColumn>();
        await LireAsync(
            "SELECT COLUMN_NAME, DATA_TYPE, NUMERIC_PRECISION, NUMERIC_SCALE FROM INFORMATION_SCHEMA.COLUMNS "
            + "WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @table ORDER BY ORDINAL_POSITION",
            table,
            reader => colonnes.Add(new PayrollTargetColumn(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetByte(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3))),
            ct);
        return colonnes;
    }

    private async Task LireAsync(string sql, string? table, Action<SqlDataReader> lire, CancellationToken ct)
    {
        var database = await _tenant.GetSageRowAsync(ct);
        try
        {
            await using var con = new SqlConnection(ExternalConnectionFactory.Build(SqlServerCredentials.Of(database), database.NomBd));
            await con.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, con);
            if (table is not null)
                cmd.Parameters.Add(new SqlParameter("@table", System.Data.SqlDbType.NVarChar, 128) { Value = table });
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                lire(reader);
        }
        catch (SqlException ex)
        {
            _logger.LogWarning(ex, "Schéma de la base de paie illisible (erreur SQL {Number}).", ex.Number);
            throw new ExternalConnectionException(ExternalDiscoveryService.Describe(ex), ex);
        }
    }
}
