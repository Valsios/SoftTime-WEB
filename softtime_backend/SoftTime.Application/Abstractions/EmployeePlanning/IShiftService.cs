using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.EmployeePlanning;

public interface IShiftService
{
    Task<IReadOnlyList<ShiftDto>> ListShiftsAsync(CancellationToken ct = default);

    /// <summary>
    /// Liste paginée du catalogue des shifts. Le catalogue reste petit, mais l'endpoint suit le
    /// même contrat que les autres listes (page bornée, total serveur) pour éviter un découpage
    /// côté client.
    /// </summary>
    Task<PagedResult<ShiftDto>> ListShiftsPagedAsync(int? page, int? pageSize, string? search = null, CancellationToken ct = default);

    Task<ShiftDto> SaveShiftAsync(ShiftDto dto, CancellationToken ct = default);

    Task DeleteShiftAsync(decimal id, CancellationToken ct = default);

    Task<ImportResultDto> ImportShiftsExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default);
}
