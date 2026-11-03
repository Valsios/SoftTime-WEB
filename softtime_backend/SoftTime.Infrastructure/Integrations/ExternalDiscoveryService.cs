using System.Security;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;

namespace SoftTime.Infrastructure.Integrations;

public class ExternalDiscoveryService : IExternalDiscoveryService
{
    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    private readonly ILogger<ExternalDiscoveryService> _logger;

    public ExternalDiscoveryService(ILogger<ExternalDiscoveryService>? logger = null)
        => _logger = logger ?? NullLogger<ExternalDiscoveryService>.Instance;

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
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            // Registre inaccessible : liste vide, l'utilisateur saisit le serveur à la main.
            _logger.LogWarning(ex, "Lecture du registre des instances SQL Server impossible : liste des serveurs vide.");
        }

        var result = servers.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(result);
    }

    public Task<IReadOnlyList<string>> ListDatabasesAsync(
        string serveur, bool sqlAuth, string? login, string? password, CancellationToken ct = default)
        => QueryStringsAsync(
            ExternalConnectionFactory.Build(serveur, "master", sqlAuth, login, password),
            "SELECT name FROM sys.databases WHERE database_id > 4 ORDER BY name", null, ct);

    public Task<IReadOnlyList<string>> ListTablesAsync(
        string serveur, string baseDb, bool sqlAuth, string? login, string? password, CancellationToken ct = default)
        => QueryStringsAsync(
            ExternalConnectionFactory.Build(serveur, baseDb, sqlAuth, login, password),
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_SCHEMA = 'dbo' ORDER BY TABLE_NAME", null, ct);

    public Task<IReadOnlyList<string>> ListColumnsAsync(
        string serveur, string baseDb, string table, bool sqlAuth, string? login, string? password, CancellationToken ct = default)
        => QueryStringsAsync(
            ExternalConnectionFactory.Build(serveur, baseDb, sqlAuth, login, password),
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @table ORDER BY ORDINAL_POSITION",
            ("@table", table), ct);

    public Task<IReadOnlyList<string>> ListDistinctValuesAsync(
        string serveur, string baseDb, string table, string column, bool sqlAuth, string? login, string? password, CancellationToken ct = default)
    {
        if (!Identifier.IsMatch(table) || !Identifier.IsMatch(column))
            throw new DomainException("La table ou la colonne contient un identifiant SQL invalide.");

        var value = $"LTRIM(RTRIM(CONVERT(nvarchar(4000), [{column}])))";
        return QueryStringsAsync(
            ExternalConnectionFactory.Build(serveur, baseDb, sqlAuth, login, password),
            $"SELECT DISTINCT {value} FROM [dbo].[{table}] WHERE NULLIF({value}, N'') IS NOT NULL ORDER BY {value}", null, ct);
    }

    /// <summary>
    /// Première colonne de chaque ligne. Une erreur SQL devient une <see cref="ExternalConnectionException"/> au message
    /// lisible : le texte SQL (qui peut citer le compte ou le serveur) ne sort jamais vers le client.
    /// </summary>
    private static async Task<IReadOnlyList<string>> QueryStringsAsync(
        string connectionString, string sql, (string Name, string Value)? parameter, CancellationToken ct)
    {
        try
        {
            await using var con = new SqlConnection(connectionString);
            await con.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, con);
            if (parameter is { } p)
                cmd.Parameters.AddWithValue(p.Name, p.Value);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var result = new List<string>();
            while (await reader.ReadAsync(ct))
                result.Add(reader.GetString(0));
            return result;
        }
        catch (SqlException ex)
        {
            throw new ExternalConnectionException(Describe(ex), ex);
        }
    }

    /// <summary>Cause lisible d'une erreur SQL, d'après son numéro (aucun texte du serveur n'est repris).</summary>
    public static string Describe(SqlException exception) => exception.Number switch
    {
        18456 or 18452 => "identifiants refusés par le serveur SQL.",
        4060 => "base inexistante ou inaccessible avec ce compte.",
        229 or 230 or 262 => "droits insuffisants sur la base.",
        -2 => "délai de connexion dépassé.",
        -1 or 2 or 26 or 40 or 53 or 1225 or 10060 or 10061 or 11001 => "serveur SQL injoignable (nom, instance ou réseau).",
        _ => $"erreur SQL n° {exception.Number} (détail dans le journal du serveur)."
    };
}
