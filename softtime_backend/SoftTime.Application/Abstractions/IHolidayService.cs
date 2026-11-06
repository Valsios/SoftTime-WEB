using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface IHolidayService
{
    Task<PagedResult<HolidayDto>> ListHolidaysPagedAsync(int? page, int? pageSize, string? search = null, CancellationToken ct = default);

    Task<IReadOnlyList<HolidayDto>> ListHolidaysAsync(CancellationToken ct = default);

    Task<HolidayDto> SaveHolidayAsync(HolidayDto dto, CancellationToken ct = default);

    Task DeleteHolidayAsync(int id, CancellationToken ct = default);

    Task ImportSageHolidaysAsync(CancellationToken ct = default);
}
