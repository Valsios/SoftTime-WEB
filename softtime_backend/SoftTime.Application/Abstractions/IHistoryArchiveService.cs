using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Abstractions;

/// <summary>
/// Archives des suppressions : copie des lignes retirées de <c>T_POINTAGE</c>, <c>T_HEUREANOMALIE</c>
/// et <c>T_HSExoImp</c>. Portage des tables « Historique* » du desktop.
///
/// Ce n'est PAS un journal d'audit (qui, quelle colonne, quelle valeur) : c'est une corbeille
/// consultable et purgeable, alimentée au moment de la suppression. L'audit colonne par colonne
/// reste assuré par <see cref="AuditService"/> (<c>suivi_*</c>).
///
/// Les méthodes <c>Archive*</c> sont réservées à un usage interne : l'appelant a déjà contrôlé
/// l'autorisation et le périmètre des lignes. Les lectures et purges d'archive, elles, revérifient
/// le tenant.
/// </summary>
public interface IHistoryArchiveService
{
    Task ArchivePunchesAsync(IReadOnlyCollection<T_POINTAGE> rows, CancellationToken ct = default);

    Task ArchiveAnomaliesAsync(IReadOnlyCollection<T_HEUREANOMALIE> rows, CancellationToken ct = default);

    Task ArchiveHsAsync(IReadOnlyCollection<T_HSExoImp> rows, CancellationToken ct = default);

    /// <summary>L'archive est filtrée sur sa propre date de suppression (quand a-t-on purgé ?).</summary>
    Task<IReadOnlyList<HistoryPointageDto>> ListPointagesAsync(PeriodRequest filter, CancellationToken ct = default);

    Task<IReadOnlyList<HistoryAnomalieDto>> ListAnomaliesAsync(PeriodRequest filter, CancellationToken ct = default);

    Task<IReadOnlyList<HistoryHsDto>> ListHsAsync(PeriodRequest filter, CancellationToken ct = default);

    /// <summary>Archive des pointages paginée (filtre SQL date + matricule, tri date de suppression).</summary>
    Task<PagedResult<HistoryPointageDto>> ListPointagesPagedAsync(PeriodRequest filter, int? page, int? pageSize, CancellationToken ct = default);

    /// <summary>Archive des anomalies paginée (filtre SQL date + matricule).</summary>
    Task<PagedResult<HistoryAnomalieDto>> ListAnomaliesPagedAsync(PeriodRequest filter, int? page, int? pageSize, CancellationToken ct = default);

    /// <summary>Archive des calculs HS paginée (filtre SQL date + matricule).</summary>
    Task<PagedResult<HistoryHsDto>> ListHsPagedAsync(PeriodRequest filter, int? page, int? pageSize, CancellationToken ct = default);

    Task<int> DeletePointagesAsync(decimal id, CancellationToken ct = default);

    Task<int> DeleteAnomaliesAsync(decimal id, CancellationToken ct = default);

    Task<int> DeleteHsAsync(decimal id, CancellationToken ct = default);

    Task<HistoryDeleteResultDto> DeleteAllPointagesAsync(CancellationToken ct = default);

    Task<HistoryDeleteResultDto> DeleteAllAnomaliesAsync(CancellationToken ct = default);

    Task<HistoryDeleteResultDto> DeleteAllHsAsync(CancellationToken ct = default);
}
