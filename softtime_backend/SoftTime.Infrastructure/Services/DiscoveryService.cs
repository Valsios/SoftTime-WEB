using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;

namespace SoftTime.Infrastructure.Services;

public sealed class DiscoveryService : IDiscoveryService
{
    private readonly IExternalDiscoveryService _discovery;
    private readonly ISourceMappingStore _sourceMappingStore;

    public DiscoveryService(
        IExternalDiscoveryService discovery,
        ISourceMappingStore sourceMappingStore)
    {
        _discovery = discovery;
        _sourceMappingStore = sourceMappingStore;
    }

    public Task<IReadOnlyList<string>> DiscoverServersAsync(CancellationToken ct = default)
        => _discovery.ListServersAsync(ct);

    public async Task<IReadOnlyList<string>> DiscoverDatabasesAsync(DiscoverDatabasesDto dto, CancellationToken ct = default)
    {
        var password = await _sourceMappingStore.ResolveStoredPasswordAsync(dto.ConnectionKind, dto.ConnectionId, dto.Serveur, dto.SqlAuth, dto.Login, dto.Password, ct);
        return await _discovery.ListDatabasesAsync(dto.Serveur, dto.SqlAuth, dto.Login, password, ct);
    }

    public async Task<IReadOnlyList<string>> DiscoverTablesAsync(DiscoverTablesDto dto, CancellationToken ct = default)
    {
        var password = await _sourceMappingStore.ResolveStoredPasswordAsync(dto.ConnectionKind, dto.ConnectionId, dto.Serveur, dto.SqlAuth, dto.Login, dto.Password, ct);
        return await _discovery.ListTablesAsync(dto.Serveur, dto.Base, dto.SqlAuth, dto.Login, password, ct);
    }

    public async Task<IReadOnlyList<string>> DiscoverColumnsAsync(DiscoverColumnsDto dto, CancellationToken ct = default)
    {
        var password = await _sourceMappingStore.ResolveStoredPasswordAsync(dto.ConnectionKind, dto.ConnectionId, dto.Serveur, dto.SqlAuth, dto.Login, dto.Password, ct);
        return await _discovery.ListColumnsAsync(dto.Serveur, dto.Base, dto.Table, dto.SqlAuth, dto.Login, password, ct);
    }
}
