using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface IDiscoveryService
{
    Task<IReadOnlyList<string>> DiscoverServersAsync(CancellationToken ct = default);

    Task<IReadOnlyList<string>> DiscoverDatabasesAsync(DiscoverDatabasesDto dto, CancellationToken ct = default);

    Task<IReadOnlyList<string>> DiscoverTablesAsync(DiscoverTablesDto dto, CancellationToken ct = default);

    Task<IReadOnlyList<string>> DiscoverColumnsAsync(DiscoverColumnsDto dto, CancellationToken ct = default);
}
