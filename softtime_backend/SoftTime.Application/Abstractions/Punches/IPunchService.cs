using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.Punches;

public interface IPunchService
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
}
