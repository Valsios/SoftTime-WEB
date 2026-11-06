using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface IPointageReportService
{
    Task<IReadOnlyList<CorrectedHourDto>> PointageAsync(ReportFilter filter, CancellationToken ct = default);

    /// <summary>État pointage paginé : filtre/ordre/découpage délégués à SQL.</summary>
    Task<PagedResult<object>> PointagePagedAsync(ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default);
}
