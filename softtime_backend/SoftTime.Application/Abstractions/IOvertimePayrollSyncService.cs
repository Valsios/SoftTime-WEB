using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface IOvertimePayrollSyncService
{
    Task SyncSageAsync(PeriodRequest request, CancellationToken ct = default);

    Task<PayrollWriteResultDto> SyncPayrollAsync(PeriodRequest request, CancellationToken ct = default);
}
