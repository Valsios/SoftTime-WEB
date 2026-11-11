using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.DataSources;

public interface ISageDatabaseService
{
    Task<IReadOnlyList<SageDbDto>> ListSageAsync(CancellationToken ct = default);

    Task<SageDbDto?> GetSageByIdAsync(int id, CancellationToken ct = default);

    Task<SageDbDto> SaveSageAsync(SageDbDto dto, CancellationToken ct = default);

    Task<ConnectionTestResultDto> TestSageConnectionAsync(TestSageConnectionDto dto, CancellationToken ct = default);

    Task DeleteSageAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Options de departement / service proposees pour une base AUTRE.
    /// Adapte au nouveau schema : elles proviennent du mapping de l'entite AFFECTATION
    /// (roles SAGE_AFF_DEPARTEMENT / SAGE_AFF_SERVICE) et non plus des colonnes MAP_*.
    /// </summary>
    Task<DepartementServiceOptionsDto> GetDepartementServiceOptionsAsync(CancellationToken ct = default);

    Task<PagedResult<SageDbDto>> ListSagePagedAsync(IReadOnlyCollection<int>? allowedIds, int? page, int? pageSize, string? search = null, CancellationToken ct = default);

    Task<ActivationResultDto> ActivateSagePairAsync(CancellationToken ct = default);
}
