using System.Runtime.ExceptionServices;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Integrations;

/// <summary>Adaptateur qui préserve intégralement l'écriture historique T_CUMSAL.</summary>
public class StandardPayrollWriter : IPayrollWriter
{
    private readonly ISagePayrollWriter _sageWriter;
    private readonly ISageContextFactory _sageFactory;
    private readonly ITenantConnectionService _tenant;
    private readonly ICodeConstanteService _catalog;
    private readonly IAuditTrail _auditTrail;

    public StandardPayrollWriter(ISagePayrollWriter sageWriter, ISageContextFactory sageFactory, ITenantConnectionService tenant, ICodeConstanteService catalog, IAuditTrail auditTrail)
    {
        _sageWriter = sageWriter;
        _sageFactory = sageFactory;
        _tenant = tenant;
        _catalog = catalog;
        _auditTrail = auditTrail;
    }

    public bool CanHandle(BddSage database) => string.Equals(database.TypeBase, "STANDARD", StringComparison.OrdinalIgnoreCase);

    public async Task<PayrollWriteResultDto> ValidateAsync(BddSage database, IReadOnlyList<HsExoDto> rows, CancellationToken ct = default)
    {
        using var sage = _sageFactory.Create(await _tenant.GetSageConnectionAsync(ct));
        var constants = sage.Constants.ToArray();
        var codes = await _catalog.GetOvertimeSageCodesAsync(ct);
        foreach (var code in new[] { codes.Exo130, codes.Exo150, codes.I130, codes.I150, codes.Ferie, codes.Dim, codes.Nuit })
            if (!constants.Any(c => string.Equals(c.CodeConstante?.Trim(), code, StringComparison.OrdinalIgnoreCase)))
                throw new DomainException($"Constante SAGE {code} introuvable.");
        return new PayrollWriteResultDto(database.NomBd ?? string.Empty, "STANDARD", 0, 0, "Configuration SAGE valide.");
    }

    public async Task<PayrollWriteResultDto> WriteAsync(BddSage database, IReadOnlyList<HsExoDto> rows, PayrollSyncContext auditContext, CancellationToken ct = default)
    {
        // Le flux ci-dessous est intentionnellement celui qui existait dans OvertimeService.
        var input = await GetInputAsync(ct);
        var categoryCounts = PayrollWritingConfiguration.Categories.ToDictionary(category => category, _ => 0, StringComparer.Ordinal);
        try
        {
            foreach (var hs in rows.Where(h => h.NumSal.HasValue))
            {
                var updates = await _sageWriter.SyncOvertimeAsync(input, hs.NumSal!.Value, OvertimeAmounts.From(hs), ct);
                foreach (var (category, count) in updates)
                    categoryCounts[category] += count;
            }
        }
        catch (PayrollSyncWriteException exception)
        {
            foreach (var (category, count) in exception.UpdatesByCategory)
                categoryCounts[category] += count;
            await WriteAuditAsync(database, auditContext, new PayrollSyncOutcome(rows.Count(h => h.NumSal.HasValue), categoryCounts.Values.Sum(), 0, categoryCounts, "ECHEC"));
            ExceptionDispatchInfo.Capture(exception.InnerException ?? exception).Throw();
            throw;
        }
        catch
        {
            await WriteAuditAsync(database, auditContext, new PayrollSyncOutcome(rows.Count(h => h.NumSal.HasValue), categoryCounts.Values.Sum(), 0, categoryCounts, "ECHEC"));
            throw;
        }

        var written = rows.Count(h => h.NumSal.HasValue);
        await WriteAuditAsync(database, auditContext, new PayrollSyncOutcome(written, categoryCounts.Values.Sum(), 0, categoryCounts, "SUCCES"));
        return new PayrollWriteResultDto(database.NomBd ?? string.Empty, "STANDARD", written, categoryCounts.Values.Sum(),
            "Synchronisation des heures supplémentaires vers SAGE terminée.");
    }

    protected virtual async Task<SagePayrollTarget> GetInputAsync(CancellationToken ct)
    {
        using var sage = _sageFactory.Create(await _tenant.GetSageConnectionAsync(ct));
        return new SagePayrollTarget(
            await _tenant.GetSageConnectionAsync(ct),
            sage.Constants.ToArray(),
            await _catalog.GetOvertimeSageCodesAsync(ct));
    }

    private Task WriteAuditAsync(BddSage database, PayrollSyncContext context, PayrollSyncOutcome bilan)
        => _auditTrail.WritePayrollSyncAsync(new PayrollSyncAudit(
            "T_CUMSAL", database.NomBd, context.PeriodFrom, context.PeriodTo, bilan.Employees, bilan.UpdatedRows, 0,
            bilan.Categories, bilan.Status, context.UserLogin, "SYNC_PAIE:STANDARD", context.Correlation), CancellationToken.None);
}
