using SoftTime.Application.DTOs;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Abstractions;

public interface IRetardReportService
{
    /// <summary>
    /// État retards : calcul complet (première entrée du jour vs planning + tolérance, comme le
    /// tableau de bord) puis écriture de <c>T_impr_RET</c> pour l'impression.
    /// </summary>
    Task<IReadOnlyList<RetardReportRow>> RetardsAsync(ReportFilter filter, CancellationToken ct = default);

    /// <summary>État retards paginé, sans écriture de la table d'impression.</summary>
    Task<PagedResult<object>> RetardsPagedAsync(ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default);

    /// <summary>Retards calculés de la période et du périmètre (mêmes règles que le dashboard).</summary>
    Task<IReadOnlyList<RetardReportRow>> CalculerRetardsAsync(ReportFilter filter, CancellationToken ct);
}
