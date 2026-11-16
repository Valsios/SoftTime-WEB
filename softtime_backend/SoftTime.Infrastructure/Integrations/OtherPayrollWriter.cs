using Microsoft.Extensions.Logging;

using SoftTime.Application.Abstractions.Audit;
using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Integrations;

/// <summary>
/// Écriture de paie d'une base AUTRE (table configurée dans T_PAIE_ECRITURE) : préflight complet, puis toutes les
/// mises à jour dans une transaction. Seules les variables cochées pour la base sont validées et écrites ; sans
/// configuration, les sept variables d'heures supplémentaires le sont, comme avant.
/// </summary>
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
    private readonly IPayrollVariableService? _variables;
    private readonly IPayrollSendLog? _sendLog;
    private readonly Func<CancellationToken, Task<IOtherPayrollDatabase>> _openDatabase;

    public OtherPayrollWriter(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IAuditTrail auditTrail,
        ILogger<OtherPayrollWriter> logger,
        IPayrollVariableService? variables = null,
        IPayrollSendLog? sendLog = null)
        : this(uow, tenant, auditTrail, logger, variables, sendLog,
            async ct => new SqlOtherPayrollDatabase(await tenant.GetSageConnectionAsync(ct)))
    {
    }

    internal OtherPayrollWriter(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IAuditTrail auditTrail,
        ILogger<OtherPayrollWriter> logger,
        IPayrollVariableService? variables,
        IPayrollSendLog? sendLog,
        Func<CancellationToken, Task<IOtherPayrollDatabase>> openDatabase)
    {
        _uow = uow;
        _tenant = tenant;
        _auditTrail = auditTrail;
        _logger = logger;
        _variables = variables;
        _sendLog = sendLog;
        _openDatabase = openDatabase;
    }

    public bool CanHandle(BddSage database) => string.Equals(database.TypeBase, "AUTRE", StringComparison.OrdinalIgnoreCase);

    public async Task<PayrollWriteResultDto> ValidateAsync(BddSage database, IReadOnlyList<HsExoDto> rows, CancellationToken ct = default)
    {
        var categories = (await SelectionAsync(database, ct)).HeuresSupActives;
        if (categories.Count == 0)
            return new PayrollWriteResultDto(database.NomBd ?? string.Empty, "AUTRE", 0, 0, AucuneVariableMessage);
        await PreflightAsync(database, rows, categories, ct);
        return new PayrollWriteResultDto(database.NomBd ?? string.Empty, "AUTRE", 0, 0, "Configuration d'écriture valide.");
    }

    internal const string AucuneVariableMessage =
        "Aucune variable d'heures supplémentaires n'est cochée pour cette base : rien n'est écrit en paie.";

    public async Task<PayrollWriteResultDto> WriteAsync(BddSage database, IReadOnlyList<HsExoDto> rows, PayrollSyncContext auditContext, CancellationToken ct = default)
    {
        var categories = (await SelectionAsync(database, ct)).HeuresSupActives;
        if (categories.Count == 0)
            return new PayrollWriteResultDto(database.NomBd ?? string.Empty, "AUTRE", 0, 0, AucuneVariableMessage);
        var plan = await PreflightAsync(database, rows, categories, ct); // aucune écriture avant la fin du préflight
        await using var db = await _openDatabase(ct);
        await db.BeginTransactionAsync(ct);
        var categoryCounts = PayrollWritingConfiguration.Categories.ToDictionary(category => category, _ => 0, StringComparer.Ordinal);
        var updates = 0;
        try
        {
            foreach (var row in rows)
            foreach (var category in categories)
            {
                var affected = await db.ExecuteUpdateAsync(UpdateSql(plan, category), OvertimePayrollCategoryMapper.ValueFor(category, row), row.Matricule!.Trim(), ct);
                if (affected != 1)
                    throw new DomainException($"La synchronisation a été annulée : {affected} ligne(s) mise(s) à jour pour le matricule {row.Matricule} et la catégorie {category}; une seule ligne était attendue.");
                updates++;
                categoryCounts[category] += affected;
            }
            await ValiderAvecMarqueurAsync(db, database, auditContext, updates, ct);
            await WriteAuditAsync(database, plan, auditContext, new PayrollSyncOutcome(rows.Count, categoryCounts.Values.Sum(), 0, categoryCounts, "SUCCES"));
            return new PayrollWriteResultDto(database.NomBd ?? string.Empty, "AUTRE", rows.Count, updates,
                "Synchronisation des heures supplémentaires terminée.");
        }
        catch
        {
            await RollbackQuietlyAsync(db);
            await QuietlyAsync(
                () => WriteAuditAsync(database, plan, auditContext, new PayrollSyncOutcome(rows.Count, 0, updates, categoryCounts, "ECHEC")),
                "Impossible d'écrire la synthèse d'échec de synchronisation de paie AUTRE.");
            throw;
        }
    }

    /// <summary>
    /// Le marqueur T_PAIE_ENVOI (base SoftTime) est enregistré AVANT la validation de la transaction de paie (base
    /// cible) : s'il ne peut pas l'être, l'exception annule toute la paie. Si la validation échoue ensuite, son issue
    /// est incertaine côté serveur : le marqueur reste, au statut INCERTAIN (il compte comme envoyé). Rien n'est
    /// enregistré si aucune ligne n'a été écrite.
    /// </summary>
    private async Task ValiderAvecMarqueurAsync(IOtherPayrollDatabase db, BddSage database, PayrollSyncContext context, int updates, CancellationToken ct)
    {
        int? envoi = null;
        if (updates > 0 && _sendLog is not null)
            envoi = await _sendLog.RecordAsync(new PayrollSend(database.NomBd ?? string.Empty, "AUTRE",
                context.PeriodFrom, context.PeriodTo, context.UserLogin, "SUCCES", updates), CancellationToken.None);
        try
        {
            await db.CommitAsync(ct);
        }
        catch
        {
            if (envoi.HasValue)
                await QuietlyAsync(() => _sendLog!.CompleteAsync(envoi.Value, "INCERTAIN", updates, CancellationToken.None),
                    "Impossible de marquer l'envoi de paie AUTRE comme incertain.");
            throw;
        }
    }

    private Task RollbackQuietlyAsync(IOtherPayrollDatabase db) =>
        QuietlyAsync(() => db.RollbackAsync(CancellationToken.None), "Échec du rollback de la synchronisation de paie AUTRE.");

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

    private async Task<PayrollVariableSelection> SelectionAsync(BddSage database, CancellationToken ct) =>
        _variables is null || string.IsNullOrWhiteSpace(database.NomBd)
            ? PayrollVariableSelection.Default
            : await _variables.GetSelectionAsync(database.NomBd, ct);

    private async Task<WritePlan> PreflightAsync(BddSage database, IReadOnlyList<HsExoDto> rows, IReadOnlyList<string> categories, CancellationToken ct)
    {
        await PayrollWritingConfiguration.EnsureStorageAsync(_uow, ct);
        var configs = await _uow.Repository<PaieEcriture>().ListAsync(c => c.BddSage == database.NomBd, ct);
        var plan = BuildPlan(database.NomBd, configs, categories);
        await using var db = await _openDatabase(ct);
        var columns = await db.ReadColumnsAsync(plan.Schema, plan.Table, ct);
        if (columns.Count == 0)
            throw new DomainException($"La table {plan.Table} n'existe pas.");
        ValidateColumns(plan, columns);
        ValidateValues(rows, plan, columns);
        await ValidateMatriculesAsync(db, plan, rows, ct);
        return plan;
    }

    internal static WritePlan BuildPlan(string? database, IReadOnlyList<PaieEcriture> configs)
        => BuildPlan(database, configs, PayrollWritingConfiguration.Categories);

    /// <summary>Plan d'écriture limité aux catégories actives : une catégorie inactive n'a aucune colonne cible.</summary>
    internal static WritePlan BuildPlan(string? database, IReadOnlyList<PaieEcriture> configs, IReadOnlyCollection<string> categories)
    {
        PayrollWritingConfiguration.ValidateConfigurationRows(configs, categories);
        var table = configs[0].TableCible;
        var matricule = configs[0].ColMatricule;
        var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var config in configs)
        {
            var category = PayrollWritingConfiguration.NormalizeCategory(config.Categorie);
            if (categories.Contains(category))
                columns.Add(category, config.ColValeur);
        }
        var (schema, targetTable) = PayrollWritingConfiguration.SplitTable(table);
        return new WritePlan(database ?? string.Empty, schema, targetTable, matricule, columns);
    }

    private static void ValidateColumns(WritePlan plan, IReadOnlyDictionary<string, OtherPayrollColumn> columns)
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

    private static void ValidateValues(IReadOnlyList<HsExoDto> rows, WritePlan plan, IReadOnlyDictionary<string, OtherPayrollColumn> columns)
    {
        var sourceDuplicates = rows.GroupBy(r => r.Matricule?.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => string.IsNullOrWhiteSpace(g.Key) || g.Count() > 1);
        if (sourceDuplicates != null)
            throw new DomainException(string.IsNullOrWhiteSpace(sourceDuplicates.Key)
                ? "Un résultat d'heures supplémentaires ne contient pas de matricule."
                : $"Plusieurs résultats d'heures supplémentaires correspondent au matricule {sourceDuplicates.Key}.");
        foreach (var row in rows)
        foreach (var category in PayrollWritingConfiguration.Categories.Where(plan.ValueColumns.ContainsKey))
        {
            var value = OvertimePayrollCategoryMapper.ValueFor(category, row);
            var target = columns[plan.ValueColumns[category]];
            if (!CanStore(value, target))
                throw new DomainException($"La valeur {value} du matricule {row.Matricule} n'est pas compatible avec la colonne {plan.ValueColumns[category]}.");
        }
    }

    private static bool CanStore(decimal value, OtherPayrollColumn target)
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

    private static async Task ValidateMatriculesAsync(IOtherPayrollDatabase db, WritePlan plan, IReadOnlyList<HsExoDto> rows, CancellationToken ct)
    {
        var sql = CountSql(plan);
        foreach (var row in rows)
        {
            var count = await db.CountAsync(sql, row.Matricule!.Trim(), ct);
            if (count == 0)
                throw new DomainException($"Aucune ligne de paie trouvée pour le matricule {row.Matricule}.");
            if (count > 1)
                throw new DomainException($"Plusieurs lignes de paie correspondent au matricule {row.Matricule}.");
        }
    }

    /// <summary>Commande historique de comptage d'un matricule (inchangée).</summary>
    internal static string CountSql(WritePlan plan)
    {
        var table = $"[{plan.Schema}].[{plan.Table}]";
        var matriculeColumn = PayrollWritingConfiguration.QuoteIdentifier(plan.MatriculeColumn);
        return $"SELECT COUNT_BIG(1) FROM {table} WHERE {matriculeColumn} = @matricule";
    }

    /// <summary>Commande historique de mise à jour d'une catégorie (inchangée).</summary>
    internal static string UpdateSql(WritePlan plan, string category)
    {
        var table = $"[{plan.Schema}].[{plan.Table}]";
        var valueColumn = PayrollWritingConfiguration.QuoteIdentifier(plan.ValueColumns[category]);
        var matriculeColumn = PayrollWritingConfiguration.QuoteIdentifier(plan.MatriculeColumn);
        return $"UPDATE {table} SET {valueColumn} = @value WHERE {matriculeColumn} = @matricule";
    }

    internal sealed record WritePlan(string Database, string Schema, string Table, string MatriculeColumn, IReadOnlyDictionary<string, string> ValueColumns);
}
