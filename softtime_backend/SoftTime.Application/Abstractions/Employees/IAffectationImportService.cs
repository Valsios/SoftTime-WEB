using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.Employees;

public interface IAffectationImportService
{
    Task<ImportResultDto> ImportAffectationsExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default);
}
