using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using System.Runtime.ExceptionServices;

namespace SoftTime.Infrastructure.Integrations;

/// <summary>Adaptateur qui préserve intégralement l'écriture historique T_CUMSAL.</summary>
public sealed class StandardPayrollWriter : IPayrollWriter
{
    private readonly ISagePayrollWriter _sageWriter;
    private readonly ISageContextFactory _sageFactory;
    private readonly TenantConnectionService _tenant;
    private readonly CatalogService _catalog;
    private readonly IAuditTrail _auditTrail;

    public StandardPayrollWriter(ISagePayrollWriter sageWriter, ISageContextFactory sageFactory, TenantConnectionService tenant, CatalogService catalog, IAuditTrail auditTrail)
    {
        _sageWriter = sageWriter;
        _sageFactory = sageFactory;
        _tenant = tenant;
        _catalog = catalog;
        _auditTrail = auditTrail;
    }

    public bool CanHandle(T_BDD_SAGE database) => string.Equals(database.TYPE_BASE, "STANDARD", StringComparison.OrdinalIgnoreCase);

    public async Task<PayrollWriteResultDto> ValidateAsync(T_BDD_SAGE database, IReadOnlyList<HsExoDto> rows, CancellationToken ct = default)
    {
        using var sage = _sageFactory.Create(await _tenant.GetSageConnectionAsync(ct));
        var constants = sage.Constants.ToArray();
        var codes = await _catalog.GetOvertimeSageCodesAsync(ct);
        foreach (var code in new[] { codes.Exo130, codes.Exo150, codes.I130, codes.I150, codes.Ferie, codes.Dim, codes.Nuit })
            if (!constants.Any(c => string.Equals(c.CodeConstante?.Trim(), code, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Constante SAGE {code} introuvable.");
        return new PayrollWriteResultDto(database.NOM_BD ?? string.Empty, "STANDARD", 0, 0, "Configuration SAGE valide.");
    }

    public async Task<PayrollWriteResultDto> WriteAsync(T_BDD_SAGE database, IReadOnlyList<HsExoDto> rows, PayrollSyncContext auditContext, CancellationToken ct = default)
    {
        // Le flux ci-dessous est intentionnellement celui qui existait dans OvertimeService.
        using var sage = _sageFactory.Create(await _tenant.GetSageConnectionAsync(ct));
        var constants = sage.Constants.ToArray();
        var connection = await _tenant.GetSageConnectionAsync(ct);
        var codes = await _catalog.GetOvertimeSageCodesAsync(ct);
        var categoryCounts = PayrollWritingConfiguration.Categories.ToDictionary(category => category, _ => 0, StringComparer.Ordinal);
        try
        {
            foreach (var hs in rows.Where(h => h.NumSal.HasValue))
            {
                var updates = await _sageWriter.SyncOvertimeAsync(connection, constants, hs.NumSal!.Value, codes,
                    hs.Exo130 ?? 0, hs.Exo150 ?? 0, hs.I130 ?? 0, hs.I150 ?? 0,
                    hs.Ferie ?? 0, hs.Nuit ?? 0, hs.Dim ?? 0, ct);
                foreach (var (category, count) in updates)
                    categoryCounts[category] += count;
            }
        }
        catch (PayrollSyncWriteException exception)
        {
            foreach (var (category, count) in exception.UpdatesByCategory)
                categoryCounts[category] += count;
            await WriteAuditAsync(database, auditContext, categoryCounts, "ECHEC", ct);
            ExceptionDispatchInfo.Capture(exception.InnerException ?? exception).Throw();
            throw;
        }
        catch
        {
            await WriteAuditAsync(database, auditContext, categoryCounts, "ECHEC", ct);
            throw;
        }

        await WriteAuditAsync(database, auditContext, categoryCounts, "SUCCES", ct);
        var written = rows.Count(h => h.NumSal.HasValue);
        return new PayrollWriteResultDto(database.NOM_BD ?? string.Empty, "STANDARD", written, categoryCounts.Values.Sum(),
            "Synchronisation des heures supplémentaires vers SAGE terminée.");
    }

    private Task WriteAuditAsync(T_BDD_SAGE database, PayrollSyncContext context, IReadOnlyDictionary<string, int> categories, string status, CancellationToken ct)
        => _auditTrail.WritePayrollSyncAsync(new PayrollSyncAudit(
            "T_CUMSAL", database.NOM_BD, context.PeriodFrom, context.PeriodTo, categories.Values.Sum(),
            categories, status, context.UserLogin, "SYNC_PAIE:STANDARD", context.Correlation), ct);
}
