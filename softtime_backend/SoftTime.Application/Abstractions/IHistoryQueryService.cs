using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface IHistoryQueryService
{
    /// <summary>L'archive est filtrée sur sa propre date de suppression (quand a-t-on purgé ?).</summary>
    Task<IReadOnlyList<HistoryPointageDto>> ListPointagesAsync(PeriodRequest filter, CancellationToken ct = default);

    Task<IReadOnlyList<HistoryAnomalieDto>> ListAnomaliesAsync(PeriodRequest filter, CancellationToken ct = default);

    Task<IReadOnlyList<HistoryHsDto>> ListHsAsync(PeriodRequest filter, CancellationToken ct = default);

    /// <summary>Archive des pointages paginée (filtre SQL date + matricule, tri date de suppression).</summary>
    Task<PagedResult<HistoryPointageDto>> ListPointagesPagedAsync(PeriodRequest filter, int? page, int? pageSize, CancellationToken ct = default);

    /// <summary>Archive des anomalies paginée (filtre SQL date + matricule).</summary>
    Task<PagedResult<HistoryAnomalieDto>> ListAnomaliesPagedAsync(PeriodRequest filter, int? page, int? pageSize, CancellationToken ct = default);

    /// <summary>Archive des calculs HS paginée (filtre SQL date + matricule).</summary>
    Task<PagedResult<HistoryHsDto>> ListHsPagedAsync(PeriodRequest filter, int? page, int? pageSize, CancellationToken ct = default);
}
