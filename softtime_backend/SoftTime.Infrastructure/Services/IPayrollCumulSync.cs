using SoftTime.Application.DTOs;

namespace SoftTime.Infrastructure.Services;

public interface IPayrollCumulSync
{
    Task<ExtrasSyncResultDto> SyncExtrasAsync(PeriodRequest request, string codeConstante, bool cantine, CancellationToken ct);
}
