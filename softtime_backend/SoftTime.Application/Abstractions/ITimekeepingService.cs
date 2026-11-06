using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

/// <summary>Contrat de <c>TimekeepingService</c>.</summary>
public interface ITimekeepingService
{
    Task<IReadOnlyList<PunchDto>> ListPunchesAsync(PeriodRequest filter, CancellationToken ct = default);

    /// <summary>
    /// Liste paginée des pointages. Le filtre, l'ordre et le découpage sont délégués à SQL
    /// (<c>COUNT(*)</c> + <c>ORDER BY … OFFSET/FETCH</c>) : seules les lignes de la page sont
    /// matérialisées. L'endpoint non paginé reste inchangé pour l'export Excel.
    /// </summary>
    Task<PagedResult<PunchDto>> ListPunchesPagedAsync(PeriodRequest filter, int? page, int? pageSize, CancellationToken ct = default);

    /// <summary>
    /// Corrige un pointage importé (date, heure, type). Refusé si la semaine est déjà validée.
    /// Le pré-traitement doit ensuite être relancé pour propager la correction.
    /// </summary>
    Task<PunchDto> UpdatePunchAsync(decimal id, PunchUpdateDto dto, CancellationToken ct = default);

    /// <summary>
    /// Purge une période de traitement : pointages (archivés), corrections automatiques et anomalies
    /// (archivées), pour la plage de matricules demandée. Portage de <c>Purger</c> du desktop, avec
    /// deux garde-fous : la période ne doit être ni validée ni déjà calculée en HS, et les
    /// corrections <b>manuelles</b> sont conservées (le desktop effaçait tout sans distinction).
    /// </summary>
    Task<PurgePunchesResultDto> PurgePunchesAsync(PeriodRequest request, CancellationToken ct = default);

    Task<ImportResultDto> ImportFromClockAsync(ImportPunchesRequest request, CancellationToken ct = default);

    Task<ImportResultDto> ImportPunchesExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default);

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

    Task<IReadOnlyList<CorrectedHourDto>> ListCorrectionsAsync(PeriodRequest filter, CancellationToken ct = default);

    /// <summary>
    /// Liste paginée des heures corrigées (pagination SQL, voir <see cref="ListPunchesPagedAsync"/> :
    /// l'endpoint non paginé reste inchangé pour ne pas tronquer l'export Excel du client).
    /// </summary>
    Task<PagedResult<CorrectedHourDto>> ListCorrectionsPagedAsync(PeriodRequest filter, int? page, int? pageSize, CancellationToken ct = default);

    Task<CorrectedHourDto> SaveCorrectionAsync(CorrectedHourDto dto, CancellationToken ct = default);

    Task DeleteCorrectionAsync(decimal id, CancellationToken ct = default);

    Task<ImportResultDto> ImportCorrectionsExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default);
}
