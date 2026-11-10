using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.Settings;

public interface IMajorationService
{
    Task<PagedResult<MajorationDto>> ListMajorationsPagedAsync(int? page, int? pageSize, string? search = null, CancellationToken ct = default);

    Task<IReadOnlyList<MajorationDto>> ListMajorationsAsync(CancellationToken ct = default);

    Task<MajorationDto> SaveMajorationAsync(MajorationDto dto, CancellationToken ct = default);
}
