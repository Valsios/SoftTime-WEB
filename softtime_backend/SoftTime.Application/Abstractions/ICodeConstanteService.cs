using SoftTime.Application.DTOs;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Abstractions;

public interface ICodeConstanteService
{
    /// <summary>
    /// Tables candidates pour le choix d'un code constante sur une base AUTRE.
    /// Adapte au nouveau schema : la table provient du mapping CONSTANT (role SAGE_CST_CODE).
    /// </summary>
    Task<IReadOnlyList<string>> ListOtherCodeConstantTablesAsync(CancellationToken ct = default);

    /// <summary>
    /// Valeurs distinctes de la colonne de code constante pour une base AUTRE.
    /// Adapte au nouveau schema : table et colonne proviennent du mapping CONSTANT
    /// (le parametre <paramref name="table"/> n'est conserve que pour la compatibilite de l'API).
    /// </summary>
    Task<IReadOnlyList<string>> ListOtherCodeConstantValuesAsync(string table, CancellationToken ct = default);

    /// <summary>
    /// Sept catégories ordonnées selon <see cref="OvertimeCodeKeys.Defaults"/> : l'ordre métier n'est
    /// pas une colonne SQL, on pagine donc en mémoire après le tri (volume négligeable).
    /// </summary>
    Task<PagedResult<CodeConstanteDto>> ListCodeConstantesPagedAsync(int? page, int? pageSize, string? search = null, CancellationToken ct = default);

    Task<IReadOnlyList<CodeConstanteDto>> ListCodeConstantesAsync(CancellationToken ct = default);

    Task<CodeConstanteDto> SaveCodeConstanteAsync(CodeConstanteDto dto, CancellationToken ct = default);

    Task<IReadOnlyList<SageConstantOptionDto>> ListSageConstantOptionsAsync(CancellationToken ct = default);

    Task<OvertimeSageCodes> GetOvertimeSageCodesAsync(CancellationToken ct = default);

    Task EnsureCodeConstantesAsync(CancellationToken ct);
}
