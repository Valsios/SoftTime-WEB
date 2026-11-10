using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.Settings;

public interface IAbsenceCodeService
{
    Task<PagedResult<AbsenceCodeDto>> ListAbsenceCodesPagedAsync(int? page, int? pageSize, string? search = null, CancellationToken ct = default);

    Task<IReadOnlyList<AbsenceCodeDto>> ListAbsenceCodesAsync(CancellationToken ct = default);

    Task<AbsenceCodeDto> SaveAbsenceCodeAsync(AbsenceCodeDto dto, CancellationToken ct = default);

    Task SyncAbsenceCodesFromSageAsync(CancellationToken ct = default);
}
