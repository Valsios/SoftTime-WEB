using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.DataSources;

public interface IPointeuseDatabaseService
{
    Task<IReadOnlyList<PointeuseDbDto>> ListPointeuseAsync(CancellationToken ct = default);

    Task<PointeuseDbDto?> GetPointeuseByIdAsync(int id, CancellationToken ct = default);

    Task<PointeuseDbDto> SavePointeuseAsync(PointeuseDbDto dto, CancellationToken ct = default);

    Task<ConnectionTestResultDto> TestPointeuseConnectionAsync(TestPointeuseConnectionDto dto, CancellationToken ct = default);

    Task DeletePointeuseAsync(int id, CancellationToken ct = default);

    Task<PagedResult<PointeuseDbDto>> ListPointeusePagedAsync(IReadOnlyCollection<int>? allowedIds, int? page, int? pageSize, string? search = null, CancellationToken ct = default);
}
