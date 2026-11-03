using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.Sage;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Application.Services;

namespace SoftTime.Application.Services.Interfaces;

/// <summary>Contrat de <see cref="OvertimeService"/> (convention §3.5 : les consommateurs dépendent de l'abstraction).</summary>
public interface IOvertimeService
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

    Task PurgeHsAsync(PeriodRequest request, CancellationToken ct = default);

    /// <summary>
    /// Purge la validation d'une semaine (lundi–dimanche) : supprime les lignes <c>T_HS_VALIDER</c>
    /// et déverrouille les heures corrigées correspondantes. Refuse si le calcul EXO/IMPO existe
    /// encore, pour ne pas laisser des montants payés sans validation.
    /// </summary>
    Task<PurgeValidationResultDto> PurgeValidationAsync(PeriodRequest request, CancellationToken ct = default);

    Task SyncSageAsync(PeriodRequest request, CancellationToken ct = default);

    Task<PayrollWriteResultDto> SyncPayrollAsync(PeriodRequest request, CancellationToken ct = default);

    /// <summary>
    /// Détail du calcul des heures supplémentaires d'un salarié : jours issus des heures corrigées,
    /// répartition hebdomadaire EXO/IMPO et allocation enregistrée. Les jours passent par le même
    /// calculateur que la validation semaine, afin que l'écran de détail explique les valeurs payées.
    /// </summary>
    Task<OvertimeDetailDto> DetailAsync(string matricule, DateTime from, DateTime to, CancellationToken ct = default);
}
