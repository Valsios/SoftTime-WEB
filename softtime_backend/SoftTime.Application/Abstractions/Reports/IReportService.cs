using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.Reports;

public interface IReportService
{
    /// <summary>Dispatcher du endpoint générique <c>GET /api/reports/{type}/paged</c>.</summary>
    Task<PagedResult<object>> PagedAsync(string type, ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default);

    Task<AbsenceInfo?> GetLeaveAsync(string matricule, DateTime date, CancellationToken ct = default);
}
