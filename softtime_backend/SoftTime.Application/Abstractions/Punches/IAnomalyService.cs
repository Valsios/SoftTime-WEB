using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.Punches;

public interface IAnomalyService
{
    Task<IReadOnlyList<AnomalyDto>> ListAnomaliesAsync(PeriodRequest filter, CancellationToken ct = default);

    /// <summary>Liste paginée des anomalies ; les anomalies sans date restent incluses.</summary>
    Task<PagedResult<AnomalyDto>> ListAnomaliesPagedAsync(PeriodRequest filter, int? page, int? pageSize, CancellationToken ct = default);

    Task<AnomalyDto?> GetAnomalyAsync(decimal id, CancellationToken ct = default);

    /// <summary>
    /// Arbitre une anomalie : renseigne les heures retenues (entrée, pause, sortie) et les drapeaux
    /// d'absence/férié. Portage de <c>CorrectionFerie</c> + <c>CorrectionES</c> du desktop, réunis
    /// dans un seul endpoint (le desktop avait deux endpoints au comportement redondant).
    /// </summary>
    Task<AnomalyDto> UpdateAnomalyAsync(decimal id, AnomalyUpdateDto dto, CancellationToken ct = default);

    /// <summary>
    /// Valide en masse : chaque anomalie sélectionnée devient une heure corrigée
    /// (<c>ORIGINE = 'MANUEL'</c>, <c>VALIDER_CORRECTION = true</c>) puis l'anomalie est supprimée.
    /// Portage de <c>ValiderCorrections</c> du desktop.
    /// </summary>
    Task<AnomalyValidationResultDto> ValidateAnomaliesAsync(IReadOnlyCollection<decimal> ids, CancellationToken ct = default);

    Task DeleteAnomalyAsync(decimal id, CancellationToken ct = default);
}
