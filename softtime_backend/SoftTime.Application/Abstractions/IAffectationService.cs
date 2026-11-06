using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface IAffectationService
{
    /// <summary>
    /// Liste paginée des affectations. Sans filtre département/service, la page est découpée en SQL
    /// puis enrichie (carte + département/service) pour les seuls matricules de la page. Avec un
    /// filtre, le jeu complet est enrichi avant le découpage, comme pour une liste calculée.
    /// </summary>
    Task<PagedResult<AffectationRowDto>> ListAffectationsPagedAsync(int? categoryId, string? departement, string? service, int? page, int? pageSize, string? search = null, CancellationToken ct = default);

    /// <summary>Jeu complet enrichi, pour l'export Excel (jamais la page affichée).</summary>
    Task<IReadOnlyList<AffectationRowDto>> ListAllAffectationsAsync(int? categoryId, string? departement, string? service, CancellationToken ct = default);

    /// <summary>Options des filtres département/service, calculées une fois au montage de l'écran.</summary>
    Task<AffectationOptionsDto> GetAffectationOptionsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<AffectationDto>> ListAffectationsAsync(int? categoryId, CancellationToken ct = default);

    Task<AffectationDto> SaveAffectationAsync(AffectationDto dto, CancellationToken ct = default);

    Task<ImportResultDto> ImportAffectationsExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default);

    Task DeleteAffectationAsync(int id, CancellationToken ct = default);
}
