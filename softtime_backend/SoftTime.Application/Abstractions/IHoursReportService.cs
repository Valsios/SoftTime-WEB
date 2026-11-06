using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface IHoursReportService
{
    Task<IReadOnlyList<HsExoDto>> HsRecapAsync(ReportFilter filter, CancellationToken ct = default);

    /// <summary>Récap HS EXO/IMPO paginé.</summary>
    Task<PagedResult<object>> HsRecapPagedAsync(ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default);

    Task<IReadOnlyList<object>> HeuresParSemaineAsync(ReportFilter filter, CancellationToken ct = default);

    /// <summary>Heures par semaine paginées (répartition EXO/IMPO calculée après la page).</summary>
    Task<PagedResult<object>> HeuresSemainePagedAsync(ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default);

    Task<IReadOnlyList<object>> HeuresDimancheAsync(ReportFilter filter, CancellationToken ct = default);

    /// <summary>Heures dimanche paginées.</summary>
    Task<PagedResult<object>> HeuresDimanchePagedAsync(ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default);
}
