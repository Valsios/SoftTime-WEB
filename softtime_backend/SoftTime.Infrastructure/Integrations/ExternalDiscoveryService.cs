using Microsoft.Data.Sql;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;
using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;

namespace SoftTime.Infrastructure.Integrations;

public class ExternalDiscoveryService : IExternalDiscoveryService
{
    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);
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

    public async Task<IReadOnlyList<string>> ListDistinctValuesAsync(
        string serveur, string baseDb, string table, string column, bool sqlAuth, string? login, string? password, CancellationToken ct = default)
    {
        if (!Identifier.IsMatch(table) || !Identifier.IsMatch(column))
            throw new InvalidOperationException("La table ou la colonne contient un identifiant SQL invalide.");

        var connStr = ExternalConnectionFactory.Build(serveur, baseDb, sqlAuth, login, password);
        await using var con = new SqlConnection(connStr);
        await con.OpenAsync(ct);
        var value = $"LTRIM(RTRIM(CONVERT(nvarchar(4000), [{column}])))";
        await using var cmd = new SqlCommand($"SELECT DISTINCT {value} FROM [dbo].[{table}] WHERE NULLIF({value}, N'') IS NOT NULL ORDER BY {value}", con);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<string>();
        while (await reader.ReadAsync(ct))
            result.Add(reader.GetString(0));
        return result;
    }
}
