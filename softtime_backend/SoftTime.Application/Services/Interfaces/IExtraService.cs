using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Application.Services;

namespace SoftTime.Application.Services.Interfaces;

/// <summary>Contrat de <see cref="ExtraService"/> (convention §3.5 : les consommateurs dépendent de l'abstraction).</summary>
public interface IExtraService
{
    Task<IReadOnlyList<CanteenDto>> ListCanteenAsync(PeriodRequest request, CancellationToken ct = default);

    /// <summary>Liste paginée des cantines calculées (filtre SQL base + dates + matricule).</summary>
    Task<PagedResult<CanteenDto>> ListCanteenPagedAsync(PeriodRequest request, int? page, int? pageSize, CancellationToken ct = default);

    Task<IReadOnlyList<TravelDto>> ListTravelAsync(PeriodRequest request, CancellationToken ct = default);

    /// <summary>Liste paginée des frais de déplacement (filtre SQL base + dates + matricule).</summary>
    Task<PagedResult<TravelDto>> ListTravelPagedAsync(PeriodRequest request, int? page, int? pageSize, CancellationToken ct = default);

    Task<IReadOnlyList<CanteenDto>> ComputeCanteenAsync(PeriodRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<TravelDto>> ComputeTravelAsync(PeriodRequest request, CancellationToken ct = default);

    /// <summary>
    /// Anomalies du calcul cantine : jour où un pointage tombe dans la fenêtre du repas alors que
    /// les heures corrigées déclarent une absence complète (matin et après-midi).
    /// Le desktop remontait une anomalie pour <b>toute</b> absence complète ; ici seules les
    /// contradictions entre les deux sources sont signalées, sinon chaque journée d'absence
    /// produirait une ligne inutile. Les anomalies sont dérivées, jamais stockées.
    /// </summary>
    Task<IReadOnlyList<CanteenAnomalyDto>> ListCanteenAnomaliesAsync(PeriodRequest request, CancellationToken ct = default);

    /// <summary>
    /// Anomalies cantine paginées : le calcul complet est fait côté serveur, puis la page est
    /// découpée <b>après</b> agrégation (jamais un Skip/Take sur les sources).
    /// </summary>
    Task<PagedResult<CanteenAnomalyDto>> ListCanteenAnomaliesPagedAsync(PeriodRequest request, int? page, int? pageSize, CancellationToken ct = default);

    /// <summary>
    /// Purge les lignes cantine de la période (portage de <c>PurgerCantines</c> du desktop, qui
    /// filtrait sur <c>DateDebut &gt;= from</c>, <c>DateFin &lt;= to</c> et la plage de matricules).
    /// Une période vide est refusée : elle viderait toute la table.
    /// </summary>
    Task<PurgeExtrasResultDto> PurgeCanteenAsync(PeriodRequest request, CancellationToken ct = default);

    /// <summary>
    /// Pousse les totaux cantine de la période dans le cumul SAGE (<c>T_CUMSAL</c>) référencé par la
    /// constante <c>CANTINE_CODE</c>. Portage de <c>SynchroniserCantines</c> du desktop.
    /// <para>
    /// Écarts assumés : (1) les valeurs poussées sont les lignes <b>stockées</b> de la période, et non
    /// le corps de la requête comme le desktop (qui faisait confiance au client) ; (2) la valeur poussée
    /// est le <b>total calculé</b> (<c>TOTAL_CANTINE</c>) alors que le desktop écrivait la constante
    /// <c>'1'</c> marquée dans son code source comme provisoire — à confirmer avec le client ;
    /// (3) la constante SAGE absente provoque un refus, au lieu du cumul arbitraire 123 du desktop.
    /// </para>
    /// </summary>
    Task<ExtrasSyncResultDto> SyncCanteenAsync(PeriodRequest request, CancellationToken ct = default);

    /// <summary>
    /// Pousse les totaux de frais de déplacement de la période dans le cumul SAGE (<c>T_CUMSAL</c>)
    /// référencé par la constante <c>HA06</c>. Portage de <c>SynchroniserAvecSageAsync</c> du desktop.
    /// </summary>
    Task<ExtrasSyncResultDto> SyncTravelAsync(PeriodRequest request, CancellationToken ct = default);
}
