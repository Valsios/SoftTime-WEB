using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Abstractions.Payroll;

public interface IPayrollWriter
{
    bool CanHandle(BddSage database);
    Task<PayrollWriteResultDto> ValidateAsync(BddSage database, IReadOnlyList<HsExoDto> rows, CancellationToken ct = default);
    Task<PayrollWriteResultDto> WriteAsync(
        BddSage database,
        IReadOnlyList<HsExoDto> rows,
        PayrollSyncContext auditContext,
        CancellationToken ct = default);
}
