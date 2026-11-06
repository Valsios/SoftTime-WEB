using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface ITravelExpenseService
{
    Task<IReadOnlyList<TravelDto>> ListTravelAsync(PeriodRequest request, CancellationToken ct = default);

    /// <summary>Liste paginée des frais de déplacement (filtre SQL base + dates + matricule).</summary>
    Task<PagedResult<TravelDto>> ListTravelPagedAsync(PeriodRequest request, int? page, int? pageSize, CancellationToken ct = default);

    Task<IReadOnlyList<TravelDto>> ComputeTravelAsync(PeriodRequest request, CancellationToken ct = default);

    /// <summary>
    /// Pousse les totaux de frais de déplacement de la période dans le cumul SAGE (<c>T_CUMSAL</c>)
    /// référencé par la constante <c>HA06</c>. Portage de <c>SynchroniserAvecSageAsync</c> du desktop.
    /// </summary>
    Task<ExtrasSyncResultDto> SyncTravelAsync(PeriodRequest request, CancellationToken ct = default);
}
