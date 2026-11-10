using SoftTime.Application.Abstractions.Payroll;

namespace SoftTime.Application.Abstractions.Audit;

/// <summary>Journal explicite des opérations réalisées hors de SoftTimeDbContext.</summary>
public interface IAuditTrail
{
    Task WritePayrollSyncAsync(PayrollSyncAudit audit, CancellationToken cancellationToken = default);
}
