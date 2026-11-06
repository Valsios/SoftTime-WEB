using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface ICardPaieService
{
    Task<PagedResult<CardPaieDto>> ListCardsPagedAsync(int? page, int? pageSize, string? search = null, CancellationToken ct = default);

    Task<IReadOnlyList<CardPaieDto>> ListCardsAsync(CancellationToken ct = default);

    Task<CardPaieDto> SaveCardAsync(CardPaieDto dto, CancellationToken ct = default);

    Task DeleteCardAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Supprime la correspondance SAGE ↔ badge identifiée par le couple de matricules
    /// (portage de <c>SupprimerParCorrespondance</c> du desktop). La comparaison des matricules est
    /// insensible au remplissage par des zéros. Le badge est optionnel : sans lui, toutes les
    /// correspondances du matricule SAGE du tenant courant sont supprimées.
    /// </summary>
    Task<int> DeleteCardByCorrespondenceAsync(string matriculeSage, string? matriculePointeuse, CancellationToken ct = default);

    Task<SyncResultDto> AutoMapCardsAsync(CancellationToken ct = default);

    Task<ImportResultDto> ImportCorrespondencesCsvAsync(IReadOnlyList<string[]> lines, CancellationToken ct = default);
}
