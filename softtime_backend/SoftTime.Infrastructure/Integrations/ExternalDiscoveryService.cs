using Microsoft.Data.Sql;
using Microsoft.Data.SqlClient;
using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;

namespace SoftTime.Infrastructure.Integrations;

public class ExternalDiscoveryService : IExternalDiscoveryService
{
    public Task<IReadOnlyList<string>> ListServersAsync(CancellationToken ct = default)
    {
        var servers = new List<string>();

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL");
            if (key != null)
            {
                foreach (var instanceName in key.GetValueNames())
                {
                    servers.Add(string.Equals(instanceName, "MSSQLSERVER", StringComparison.OrdinalIgnoreCase)
                        ? Environment.MachineName
                        : $"{Environment.MachineName}\\{instanceName}");
                }
            }
        }
        catch
        {
            // Registre inaccessible : liste vide, l'utilisateur devra rafraîchir ou l'admin système vérifiera les droits.
        }

        var result = servers.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(result);
    }

    public async Task<IReadOnlyList<string>> ListDatabasesAsync(
        string serveur, bool sqlAuth, string? login, string? password, CancellationToken ct = default)
    {
        var connStr = ExternalConnectionFactory.Build(serveur, "master", sqlAuth, login, password);
        await using var con = new SqlConnection(connStr);
        await con.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT name FROM sys.databases WHERE database_id > 4 ORDER BY name", con);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<string>();
        while (await reader.ReadAsync(ct))
            result.Add(reader.GetString(0));
        return result;
    }

    public async Task<IReadOnlyList<string>> ListTablesAsync(
        string serveur, string baseDb, bool sqlAuth, string? login, string? password, CancellationToken ct = default)
    {
        var connStr = ExternalConnectionFactory.Build(serveur, baseDb, sqlAuth, login, password);
        await using var con = new SqlConnection(connStr);
        await con.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_SCHEMA = 'dbo' ORDER BY TABLE_NAME", con);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<string>();
        while (await reader.ReadAsync(ct))
            result.Add(reader.GetString(0));
        return result;
    }

    public async Task<IReadOnlyList<string>> ListColumnsAsync(
        string serveur, string baseDb, string table, bool sqlAuth, string? login, string? password, CancellationToken ct = default)
    {
        var connStr = ExternalConnectionFactory.Build(serveur, baseDb, sqlAuth, login, password);
        await using var con = new SqlConnection(connStr);
        await con.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @table ORDER BY ORDINAL_POSITION", con);
        cmd.Parameters.AddWithValue("@table", table);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<string>();
        while (await reader.ReadAsync(ct))
            result.Add(reader.GetString(0));
        return result;
    }
}