using System.Data;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Integrations;

public sealed class OtherPayrollWriter : IPayrollWriter
{
    private static readonly HashSet<string> NumericTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "bigint", "int", "smallint", "tinyint", "decimal", "numeric", "money", "smallmoney", "float", "real"
    };
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IAuditTrail _auditTrail;
    private readonly ILogger<OtherPayrollWriter> _logger;

    public OtherPayrollWriter(IUnitOfWork uow, ITenantConnectionService tenant, IAuditTrail auditTrail, ILogger<OtherPayrollWriter> logger)
    {
        _uow = uow;
        _tenant = tenant;
        _auditTrail = auditTrail;
        _logger = logger;
    }

    public bool CanHandle(BddSage database) => string.Equals(database.TypeBase, "AUTRE", StringComparison.OrdinalIgnoreCase);

    public async Task<PayrollWriteResultDto> ValidateAsync(BddSage database, IReadOnlyList<HsExoDto> rows, CancellationToken ct = default)
    {
        await PreflightAsync(database, rows, ct);
        return new PayrollWriteResultDto(database.NomBd ?? string.Empty, "AUTRE", 0, 0, "Configuration d'écriture valide.");
    }

    public async Task<PayrollWriteResultDto> WriteAsync(BddSage database, IReadOnlyList<HsExoDto> rows, PayrollSyncContext auditContext, CancellationToken ct = default)
    {
        var plan = await PreflightAsync(database, rows, ct); // aucune écriture avant la fin du préflight
        await using var connection = new SqlConnection(await _tenant.GetSageConnectionAsync(ct));
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        var categoryCounts = PayrollWritingConfiguration.Categories.ToDictionary(category => category, _ => 0, StringComparer.Ordinal);
        var updates = 0;
        try
        {
            foreach (var row in rows)
            foreach (var category in PayrollWritingConfiguration.Categories)
            {
                var affected = await UpdateAsync(connection, transaction, plan, category, row, ct);
                if (affected != 1)
                    throw new DomainException($"La synchronisation a été annulée : {affected} ligne(s) mise(s) à jour pour le matricule {row.Matricule} et la catégorie {category}; une seule ligne était attendue.");
                updates++;
                categoryCounts[category] += affected;
            }
            await transaction.CommitAsync(ct);
            await WriteAuditAsync(database, plan, auditContext, new PayrollSyncOutcome(rows.Count, categoryCounts.Values.Sum(), 0, categoryCounts, "SUCCES"));
            return new PayrollWriteResultDto(database.NomBd ?? string.Empty, "AUTRE", rows.Count, updates,
                "Synchronisation des heures supplémentaires terminée.");
        }
        catch
        {
            await RollbackQuietlyAsync(transaction);
            await QuietlyAsync(
                () => WriteAuditAsync(database, plan, auditContext, new PayrollSyncOutcome(rows.Count, 0, updates, categoryCounts, "ECHEC")),
                "Impossible d'écrire la synthèse d'échec de synchronisation de paie AUTRE.");
            throw;
        }
    }

    private Task RollbackQuietlyAsync(SqlTransaction transaction) =>
        QuietlyAsync(() => transaction.RollbackAsync(CancellationToken.None), "Échec du rollback de la synchronisation de paie AUTRE.");

    /// <summary>Exécute une action de repli après échec ; son propre échec est journalisé sans masquer l'erreur initiale.</summary>
    private async Task QuietlyAsync(Func<Task> action, string erreur)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Erreur}", erreur);
        }
    }

    private Task WriteAuditAsync(BddSage database, WritePlan plan, PayrollSyncContext context, PayrollSyncOutcome bilan)
        => _auditTrail.WritePayrollSyncAsync(new PayrollSyncAudit(
            $"{plan.Schema}.{plan.Table}", database.NomBd, context.PeriodFrom, context.PeriodTo,
            bilan.Employees, bilan.UpdatedRows, bilan.CancelledRows, bilan.Categories, bilan.Status,
            context.UserLogin, "SYNC_PAIE:AUTRE", context.Correlation), CancellationToken.None);

    private async Task<WritePlan> PreflightAsync(BddSage database, IReadOnlyList<HsExoDto> rows, CancellationToken ct)
    {
        await PayrollWritingConfiguration.EnsureStorageAsync(_uow, ct);
        var configs = await _uow.Repository<PaieEcriture>().ListAsync(c => c.BddSage == database.NomBd, ct);
        var plan = BuildPlan(database.NomBd, configs);
        await using var connection = new SqlConnection(await _tenant.GetSageConnectionAsync(ct));
        await connection.OpenAsync(ct);
        var columns = await ReadColumnsAsync(connection, plan, ct);
        ValidateColumns(plan, columns);
        ValidateValues(rows, plan, columns);
        await ValidateMatriculesAsync(connection, plan, rows, ct);
        return plan;
    }

    internal static WritePlan BuildPlan(string? database, IReadOnlyList<PaieEcriture> configs)
    {
        PayrollWritingConfiguration.ValidateConfigurationRows(configs);
        var table = configs[0].TableCible;
        var matricule = configs[0].ColMatricule;
        var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var config in configs)
        {
            var category = PayrollWritingConfiguration.NormalizeCategory(config.Categorie);
            columns.Add(category, config.ColValeur);
        }
        var (schema, targetTable) = PayrollWritingConfiguration.SplitTable(table);
        return new WritePlan(database ?? string.Empty, schema, targetTable, matricule, columns);
    }

    private static async Task<Dictionary<string, ColumnInfo>> ReadColumnsAsync(SqlConnection connection, WritePlan plan, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            SELECT COLUMN_NAME, DATA_TYPE, NUMERIC_PRECISION, NUMERIC_SCALE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
            """, connection);
        cmd.Parameters.AddWithValue("@schema", plan.Schema);
        cmd.Parameters.AddWithValue("@table", plan.Table);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new Dictionary<string, ColumnInfo>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(ct))
        {
            result[reader.GetString(0)] = new ColumnInfo(reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetByte(2), reader.IsDBNull(3) ? null : reader.GetInt32(3));
        }
        if (result.Count == 0)
            throw new DomainException($"La table {plan.Table} n'existe pas.");
        return result;
    }

    private static void ValidateColumns(WritePlan plan, IReadOnlyDictionary<string, ColumnInfo> columns)
    {
        if (!columns.ContainsKey(plan.MatriculeColumn))
            throw new DomainException($"La colonne {plan.MatriculeColumn} n'existe pas.");
        foreach (var (category, column) in plan.ValueColumns)
        {
            if (!columns.TryGetValue(column, out var info))
                throw new DomainException($"La colonne {column} de la catégorie {category} n'existe pas.");
            if (!NumericTypes.Contains(info.DataType))
                throw new DomainException($"La colonne {column} de la catégorie {category} doit être numérique.");
        }
    }

    private static void ValidateValues(IReadOnlyList<HsExoDto> rows, WritePlan plan, IReadOnlyDictionary<string, ColumnInfo> columns)
    {
        var sourceDuplicates = rows.GroupBy(r => r.Matricule?.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => string.IsNullOrWhiteSpace(g.Key) || g.Count() > 1);
        if (sourceDuplicates != null)
            throw new DomainException(string.IsNullOrWhiteSpace(sourceDuplicates.Key)
                ? "Un résultat d'heures supplémentaires ne contient pas de matricule."
                : $"Plusieurs résultats d'heures supplémentaires correspondent au matricule {sourceDuplicates.Key}.");
        foreach (var row in rows)
        foreach (var category in PayrollWritingConfiguration.Categories)
        {
            var value = OvertimePayrollCategoryMapper.ValueFor(category, row);
            var target = columns[plan.ValueColumns[category]];
            if (!CanStore(value, target))
                throw new DomainException($"La valeur {value} du matricule {row.Matricule} n'est pas compatible avec la colonne {plan.ValueColumns[category]}.");
        }
    }

    private static bool CanStore(decimal value, ColumnInfo target)
    {
        if (target.DataType is "tinyint" or "smallint" or "int" or "bigint")
            return value == decimal.Truncate(value);
        if (target.DataType is not ("decimal" or "numeric") || target.Precision is null || target.Scale is null)
            return true;
        var scale = GetScale(value);
        if (scale > target.Scale) return false;
        var integerDigits = value == 0 ? 1 : decimal.Truncate(decimal.Abs(value)).ToString(System.Globalization.CultureInfo.InvariantCulture).Length;
        return integerDigits + scale <= target.Precision;
    }

    private static int GetScale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0x7F;

    private static async Task ValidateMatriculesAsync(SqlConnection connection, WritePlan plan, IReadOnlyList<HsExoDto> rows, CancellationToken ct)
    {
        var table = $"[{plan.Schema}].[{plan.Table}]";
        var matriculeColumn = PayrollWritingConfiguration.QuoteIdentifier(plan.MatriculeColumn);
        foreach (var row in rows)
        {
            await using var cmd = new SqlCommand($"SELECT COUNT_BIG(1) FROM {table} WHERE {matriculeColumn} = @matricule", connection);
            cmd.Parameters.Add("@matricule", SqlDbType.NVarChar, 256).Value = row.Matricule!.Trim();
            var count = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
            if (count == 0)
                throw new DomainException($"Aucune ligne de paie trouvée pour le matricule {row.Matricule}.");
            if (count > 1)
                throw new DomainException($"Plusieurs lignes de paie correspondent au matricule {row.Matricule}.");
        }
    }

    private static async Task<int> UpdateAsync(SqlConnection connection, SqlTransaction transaction, WritePlan plan, string category, HsExoDto row, CancellationToken ct)
    {
        var table = $"[{plan.Schema}].[{plan.Table}]";
        var valueColumn = PayrollWritingConfiguration.QuoteIdentifier(plan.ValueColumns[category]);
        var matriculeColumn = PayrollWritingConfiguration.QuoteIdentifier(plan.MatriculeColumn);
        await using var cmd = new SqlCommand($"UPDATE {table} SET {valueColumn} = @value WHERE {matriculeColumn} = @matricule", connection, transaction);
        cmd.Parameters.Add("@value", SqlDbType.Decimal).Value = OvertimePayrollCategoryMapper.ValueFor(category, row);
        cmd.Parameters["@value"].Precision = 18;
        cmd.Parameters["@value"].Scale = 4;
        cmd.Parameters.Add("@matricule", SqlDbType.NVarChar, 256).Value = row.Matricule!.Trim();
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    internal sealed record WritePlan(string Database, string Schema, string Table, string MatriculeColumn, IReadOnlyDictionary<string, string> ValueColumns);
    private sealed record ColumnInfo(string DataType, byte? Precision, int? Scale);
}
