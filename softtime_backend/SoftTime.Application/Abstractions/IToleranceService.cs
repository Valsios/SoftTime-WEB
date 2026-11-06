using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface IToleranceService
{
    Task<PagedResult<ToleranceDto>> ListTolerancesPagedAsync(int? categoryId, int? page, int? pageSize, string? search = null, CancellationToken ct = default);

    Task<IReadOnlyList<ToleranceDto>> ListTolerancesAsync(int? categoryId, CancellationToken ct = default);

    Task<ToleranceDto> SaveToleranceAsync(ToleranceDto dto, CancellationToken ct = default);

    Task DeleteToleranceAsync(int id, CancellationToken ct = default);
}
