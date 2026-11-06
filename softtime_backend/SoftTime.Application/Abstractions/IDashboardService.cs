using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface IDashboardService
{
    Task<DashboardStatsDto> GetStatsAsync(PeriodRequest filter, string? categorie, string periode, CancellationToken ct = default);

    /// <summary>Valeurs distinctes proposées par les filtres de la page (département, catégorie).</summary>
    Task<DashboardFiltersDto> GetFiltersAsync(CancellationToken ct = default);
}
