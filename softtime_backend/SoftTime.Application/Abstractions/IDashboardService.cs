using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

/// <summary>
/// Tableau de bord analytique. Portage de l'endpoint <c>Stats</c> du desktop
/// (<c>DashboardController</c>), seules les trois autres routes de ce contrôleur étant
/// commentées (code mort qui lisait la table brute de la pointeuse).
///
/// Les absences « explicites » et les retards se déduisent des heures corrigées, du planning et
/// des tolérances — exactement les mêmes règles que <see cref="IWeeklyValidationService"/> pour la semaine,
/// afin que le tableau de bord ne raconte pas autre chose que la paie.
/// </summary>
public interface IDashboardService
{
    Task<DashboardStatsDto> GetStatsAsync(PeriodRequest filter, string? categorie, string periode, CancellationToken ct = default);

    /// <summary>Valeurs distinctes proposées par les filtres de la page (département, catégorie).</summary>
    Task<DashboardFiltersDto> GetFiltersAsync(CancellationToken ct = default);
}
