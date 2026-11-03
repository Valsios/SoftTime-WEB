using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Application.Services;

namespace SoftTime.Application.Services.Interfaces;

/// <summary>Contrat de <see cref="HistoryArchiveService"/> (convention §3.5 : les consommateurs dépendent de l'abstraction).</summary>
public interface IHistoryArchiveService
{
    Task ArchivePunchesAsync(IReadOnlyCollection<T_POINTAGE> rows, CancellationToken ct = default);

    Task ArchiveAnomaliesAsync(IReadOnlyCollection<T_HEUREANOMALIE> rows, CancellationToken ct = default);

    Task ArchiveHsAsync(IReadOnlyCollection<T_HSExoImp> rows, CancellationToken ct = default);

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

    Task<int> DeletePointagesAsync(decimal id, CancellationToken ct = default);

    Task<int> DeleteAnomaliesAsync(decimal id, CancellationToken ct = default);

    Task<int> DeleteHsAsync(decimal id, CancellationToken ct = default);

    Task<HistoryDeleteResultDto> DeleteAllPointagesAsync(CancellationToken ct = default);

    Task<HistoryDeleteResultDto> DeleteAllAnomaliesAsync(CancellationToken ct = default);

    Task<HistoryDeleteResultDto> DeleteAllHsAsync(CancellationToken ct = default);
}
