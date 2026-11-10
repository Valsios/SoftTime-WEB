using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.Punches;

public interface ICorrectedHoursService
{
    Task<IReadOnlyList<CorrectedHourDto>> ListCorrectionsAsync(PeriodRequest filter, CancellationToken ct = default);

    /// <summary>
    /// Liste paginée des heures corrigées (pagination SQL, voir <see cref="ListPunchesPagedAsync"/> :
    /// l'endpoint non paginé reste inchangé pour ne pas tronquer l'export Excel du client).
    /// </summary>
    Task<PagedResult<CorrectedHourDto>> ListCorrectionsPagedAsync(PeriodRequest filter, int? page, int? pageSize, CancellationToken ct = default);

    Task<CorrectedHourDto> SaveCorrectionAsync(CorrectedHourDto dto, CancellationToken ct = default);

    Task DeleteCorrectionAsync(decimal id, CancellationToken ct = default);

    Task<ImportResultDto> ImportCorrectionsExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default);
}
