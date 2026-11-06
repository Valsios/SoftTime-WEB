using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface IHistoryPurgeService
{
    Task<int> DeletePointagesAsync(decimal id, CancellationToken ct = default);

    Task<int> DeleteAnomaliesAsync(decimal id, CancellationToken ct = default);

    Task<int> DeleteHsAsync(decimal id, CancellationToken ct = default);

    Task<HistoryDeleteResultDto> DeleteAllPointagesAsync(CancellationToken ct = default);

    Task<HistoryDeleteResultDto> DeleteAllAnomaliesAsync(CancellationToken ct = default);

    Task<HistoryDeleteResultDto> DeleteAllHsAsync(CancellationToken ct = default);
}
