namespace SoftTime.Application.Abstractions;

public interface IExternalDiscoveryService
{
    Task<IReadOnlyList<string>> ListServersAsync(CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListDatabasesAsync(
        string serveur, bool sqlAuth, string? login, string? password, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListTablesAsync(
        string serveur, string baseDb, bool sqlAuth, string? login, string? password, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListColumnsAsync(
        string serveur, string baseDb, string table, bool sqlAuth, string? login, string? password, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListDistinctValuesAsync(
        string serveur, string baseDb, string table, string column, bool sqlAuth, string? login, string? password, CancellationToken ct = default);
}
