using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.Extras;

public interface IPayrollCumulSync
{
    Task<ExtrasSyncResultDto> SyncExtrasAsync(PeriodRequest request, string codeConstante, bool cantine, CancellationToken ct);
}
