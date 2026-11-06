using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface IWeeklyValidationService
{
    Task<IReadOnlyList<WeeklyHsDto>> PreviewWeekAsync(PeriodRequest request, CancellationToken ct = default);

    /// <summary>
    /// Aperçu paginé de la validation semaine. Lecture seule : contrairement à
    /// <see cref="PreviewWeekAsync"/>, ne persiste pas les lignes non validées, afin de ne pas
    /// écrire à chaque changement de page. Les semaines déjà validées sont renvoyées telles quelles
    /// (elles ne sont plus recalculées) pour pouvoir les annuler depuis l'écran.
    /// </summary>
    Task<PagedResult<WeeklyHsDto>> PreviewWeekPagedAsync(PeriodRequest request, int? page, int? pageSize, CancellationToken ct = default);

    /// <summary>
    /// Valide (ou annule la validation de) la semaine pour les salariés sélectionnés.
    /// La validation fige la semaine (<c>VALIDER_HS</c>), l'annulation la rouvre (synthèse §5).
    /// </summary>
    Task ValidateWeekAsync(WeeklyValidationRequest request, bool validate, CancellationToken ct = default);

    /// <summary>
    /// Purge la validation d'une semaine (lundi–dimanche) : supprime les lignes <c>T_HS_VALIDER</c>
    /// et déverrouille les heures corrigées correspondantes. Refuse si le calcul EXO/IMPO existe
    /// encore, pour ne pas laisser des montants payés sans validation.
    /// </summary>
    Task<PurgeValidationResultDto> PurgeValidationAsync(PeriodRequest request, CancellationToken ct = default);
}
