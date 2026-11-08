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
        var password = await _sourceMappingStore.ResolveStoredPasswordAsync(new ConnectionAttempt(dto.ConnectionKind, dto.ConnectionId, dto.Serveur, dto.SqlAuth, dto.Login, dto.Password), ct);
        return await _discovery.ListDatabasesAsync(new SqlServerCredentials(dto.Serveur, dto.SqlAuth, dto.Login, password), ct);
    }

    public async Task<IReadOnlyList<string>> DiscoverTablesAsync(DiscoverTablesDto dto, CancellationToken ct = default)
    {
        var password = await _sourceMappingStore.ResolveStoredPasswordAsync(new ConnectionAttempt(dto.ConnectionKind, dto.ConnectionId, dto.Serveur, dto.SqlAuth, dto.Login, dto.Password), ct);
        return await _discovery.ListTablesAsync(new SqlServerCredentials(dto.Serveur, dto.SqlAuth, dto.Login, password), dto.Base, ct);
    }

    public async Task<IReadOnlyList<string>> DiscoverColumnsAsync(DiscoverColumnsDto dto, CancellationToken ct = default)
    {
        var password = await _sourceMappingStore.ResolveStoredPasswordAsync(new ConnectionAttempt(dto.ConnectionKind, dto.ConnectionId, dto.Serveur, dto.SqlAuth, dto.Login, dto.Password), ct);
        return await _discovery.ListColumnsAsync(new SqlServerCredentials(dto.Serveur, dto.SqlAuth, dto.Login, password), dto.Base, dto.Table, ct);
    }
}
