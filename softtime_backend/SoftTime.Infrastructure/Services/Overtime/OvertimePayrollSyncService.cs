using SoftTime.Application.Abstractions.Overtime;
using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services.Overtime;

public sealed class OvertimePayrollSyncService : IOvertimePayrollSyncService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IPayrollWriterResolver _payrollWriters;
    private readonly ICurrentUser? _user;
    private readonly IOvertimeWeekContext _overtimeWeekContext;

    public OvertimePayrollSyncService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IPayrollWriterResolver payrollWriters,
        ICurrentUser? user,
        IOvertimeWeekContext overtimeWeekContext)
    {
        _uow = uow;
        _tenant = tenant;
        _payrollWriters = payrollWriters;
        _user = user;
        _overtimeWeekContext = overtimeWeekContext;
    }

    // Endpoint historique : conserve pour les integrations existantes, uniquement pour STANDARD.
    public async Task SyncSageAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageRow = await _tenant.GetSageRowAsync(ct);
        if (!string.Equals(sageRow.TypeBase, "STANDARD", StringComparison.OrdinalIgnoreCase))
            throw new DomainException(
                "La synchronisation SAGE est disponible uniquement pour une base STANDARD. Utilisez la synchronisation de paie pour une base AUTRE.");
        await SyncPayrollAsync(request, ct);
    }

    // Ecriture de paie unifiee : le resolveur choisit le writer STANDARD (T_CUMSAL) ou AUTRE (T_PAIE_ECRITURE).
    public async Task<PayrollWriteResultDto> SyncPayrollAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        _overtimeWeekContext.EnsureMaxOneMonth(request);
        var sageRow = await _tenant.GetSageRowAsync(ct);
        var rows = await _uow.Repository<HsExoImp>().ListReadOnlyAsync(h =>
            h.BddSage == _tenant.SageDb && h.PeriodeDebut == request.From.Date && h.PeriodeFin == request.To.Date, ct);

        if (!string.IsNullOrEmpty(request.Departement))
        {
            var cards = await _overtimeWeekContext.TargetCardsAsync(request, ct);
            var targetMatricules = cards.Select(c => c.SageMatricule).Where(m => !string.IsNullOrEmpty(m)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            rows = rows.Where(h => h.Matricule != null && targetMatricules.Contains(h.Matricule)).ToList();
        }

        var dtos = rows
            .Select(h => new HsExoDto(h.Oid, h.Matricule, h.NumSal, h.Exo130, h.Exo150, h.I130, h.I150,
                h.Ferie, h.Nuit, h.Dim, h.Retard, h.Absence))
            .ToList();
        var auditContext = new PayrollSyncContext(request.From.Date, request.To.Date, _user?.Login, Guid.NewGuid());
        return await _payrollWriters.Resolve(sageRow).WriteAsync(sageRow, dtos, auditContext, ct);
    }
}
