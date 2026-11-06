namespace SoftTime.Application.Abstractions;

/// <summary>Journal explicite des opérations réalisées hors de SoftTimeDbContext.</summary>
public interface IAuditTrail
{
    Task WritePayrollSyncAsync(PayrollSyncAudit audit, CancellationToken cancellationToken = default);
}
