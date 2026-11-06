using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface IOvertimeExoImpoService
{
    Task<IReadOnlyList<HsExoDto>> ListExoAsync(PeriodRequest request, CancellationToken ct = default);

    /// <summary>
    /// Liste paginée des cumuls EXO/IMPO. Le filtre est traduit en SQL ; le filtre
    /// département est résolu via les cartes cibles (même sémantique que
    /// <see cref="ListExoAsync"/>). L'endpoint non paginé reste utilisé pour l'export.
    /// </summary>
    Task<PagedResult<HsExoDto>> ListExoPagedAsync(PeriodRequest request, int? page, int? pageSize, CancellationToken ct = default);

    /// <summary>
    /// Étapes 3 et 4 de la chaîne (synthèse §6 et §7) : reconstruit <c>T_HSSemaine</c> à partir des
    /// semaines <b>validées</b> (8 h à 130 %, au-delà à 150 %), puis agrège les compteurs de la période
    /// dans <c>T_HSExoImp</c> avec le plafond de 20 h exonérées.
    /// </summary>
    Task<IReadOnlyList<HsExoDto>> CalculateExoImpoAsync(PeriodRequest request, CancellationToken ct = default);
}
