namespace SoftTime.Application.Abstractions;

public interface IExternalDiscoveryService
{
    Task<IReadOnlyList<string>> ListServersAsync(CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListDatabasesAsync(
        SqlServerCredentials credentials, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListTablesAsync(
        SqlServerCredentials credentials, string baseDb, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListColumnsAsync(
        SqlServerCredentials credentials, string baseDb, string table, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListDistinctValuesAsync(
        SqlServerCredentials credentials, string baseDb, string table, string column, CancellationToken ct = default);
}
