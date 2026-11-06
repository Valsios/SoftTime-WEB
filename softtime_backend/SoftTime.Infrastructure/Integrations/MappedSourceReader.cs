using System.Text;
using System.Text.RegularExpressions;

using Microsoft.Data.SqlClient;

using SoftTime.Application.Abstractions;

namespace SoftTime.Infrastructure.Integrations;

public sealed class MappedSourceReader : IMappedSourceReader
{
    private static readonly Regex SafeIdentifier = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    public async Task<IReadOnlyList<MappedRow>> ReadAsync(
        string connectionString,
        string sourceTable,
        IReadOnlyDictionary<string, string> roleToColumn,
        MappedQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceTable) || !SafeIdentifier.IsMatch(sourceTable))
            throw new DomainException($"Nom de table source invalide : {sourceTable}");
        if (roleToColumn is null || roleToColumn.Count == 0)
            throw new DomainException("Aucun mapping de colonne fourni.");

        // Toute la validation précède l'ouverture de la connexion : un mapping invalide n'atteint pas la source.
        var select = BuildSelect(sourceTable, roleToColumn, query);
        var filterColumn = FilterColumn(query);
        await using var cmd = new SqlCommand();
        if (filterColumn is not null && query is not null)
            AppendFilter(select, cmd, filterColumn, query);
        cmd.CommandText = select.ToString();

        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync(cancellationToken);
        cmd.Connection = con;
        return await ReadRowsAsync(cmd, cancellationToken);
    }

    /// <summary>SELECT [colonne] AS [rôle] des colonnes mappées, identifiants contrôlés.</summary>
    private static StringBuilder BuildSelect(string sourceTable, IReadOnlyDictionary<string, string> roleToColumn, MappedQuery? query)
    {
        var select = new StringBuilder("SELECT ");
        if (query?.Top is int top and > 0)
            select.Append($"TOP {top} ");

        var colonnes = roleToColumn.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToList();
        var invalide = colonnes.FirstOrDefault(kv => !SafeIdentifier.IsMatch(kv.Value));
        if (invalide.Value is not null)
            throw new DomainException($"Nom de colonne source invalide : {invalide.Value}");
        if (colonnes.Count == 0)
            throw new DomainException("Aucune colonne source valide dans le mapping.");

        select.Append(string.Join(", ", colonnes.Select(kv => $"[{kv.Value}] AS [{kv.Key}]")));
        select.Append($" FROM [{sourceTable}]");
        return select;
    }

    private static string? FilterColumn(MappedQuery? query)
    {
        if (string.IsNullOrWhiteSpace(query?.FilterColumn))
            return null;
        var filterColumn = query.FilterColumn;
        if (!SafeIdentifier.IsMatch(filterColumn))
            throw new DomainException($"Nom de colonne de filtre invalide : {filterColumn}");
        return filterColumn;
    }

    /// <summary>Filtre d'égalité (texte) ou de plage de dates, valeurs toujours paramétrées.</summary>
    private static void AppendFilter(StringBuilder select, SqlCommand cmd, string filterColumn, MappedQuery query)
    {
        if (query.FilterKind == MappedFilterKind.Equals)
        {
            select.Append($" WHERE CAST([{filterColumn}] AS NVARCHAR(128)) = @eq");
            cmd.Parameters.AddWithValue("@eq", (object?)query.EqualsValue ?? DBNull.Value);
        }
        else if (query.FilterKind == MappedFilterKind.Range)
        {
            select.Append($" WHERE CAST([{filterColumn}] AS DATETIME2) >= @from AND CAST([{filterColumn}] AS DATETIME2) < @to");
            cmd.Parameters.AddWithValue("@from", (object?)query.RangeFrom ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@to", (object?)query.RangeTo ?? DBNull.Value);
        }
    }

    private static async Task<List<MappedRow>> ReadRowsAsync(SqlCommand cmd, CancellationToken ct)
    {
        var rows = new List<MappedRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
                dict[reader.GetName(i)] = reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i));
            rows.Add(new MappedRow(dict));
        }
        return rows;
    }
}
