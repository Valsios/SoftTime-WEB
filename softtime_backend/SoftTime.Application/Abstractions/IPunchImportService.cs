using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface IPunchImportService
{
    Task<ImportResultDto> ImportFromClockAsync(ImportPunchesRequest request, CancellationToken ct = default);

    Task<ImportResultDto> ImportPunchesExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default);
}
