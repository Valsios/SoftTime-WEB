using SoftTime.Application.DTOs;

namespace SoftTime.Application.Services.Interfaces;

/// <summary>Bases SAGE et pointeuse visibles par le compte connecté (droits détaillés <c>{SYSTEME}:{base}:READ</c>).</summary>
public interface IDatabaseListingService
{
    Task<IReadOnlyList<SageDbDto>> ListSageAsync(CancellationToken ct = default);

    Task<PagedResult<SageDbDto>> ListSagePagedAsync(int? page, int? pageSize, string? search, CancellationToken ct = default);

    Task<IReadOnlyList<PointeuseDbDto>> ListPointeuseAsync(CancellationToken ct = default);

    Task<PagedResult<PointeuseDbDto>> ListPointeusePagedAsync(int? page, int? pageSize, string? search, CancellationToken ct = default);
}
