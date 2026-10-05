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
            throw new InvalidOperationException($"Nom de table source invalide : {sourceTable}");
        if (roleToColumn is null || roleToColumn.Count == 0)
            throw new InvalidOperationException("Aucun mapping de colonne fourni.");

        var select = new StringBuilder("SELECT ");
        if (query?.Top is int top and > 0)
            select.Append($"TOP {top} ");

        var hasColumn = false;
        foreach (var kv in roleToColumn)
        {
            if (string.IsNullOrWhiteSpace(kv.Value)) continue;
            if (!SafeIdentifier.IsMatch(kv.Value))
                throw new InvalidOperationException($"Nom de colonne source invalide : {kv.Value}");
            if (hasColumn) select.Append(", ");
            select.Append($"[{kv.Value}] AS [{kv.Key}]");
            hasColumn = true;
        }
        if (!hasColumn)
            throw new InvalidOperationException("Aucune colonne source valide dans le mapping.");

        select.Append($" FROM [{sourceTable}]");

        string? filterColumn = null;
        if (!string.IsNullOrWhiteSpace(query?.FilterColumn))
        {
            filterColumn = query!.FilterColumn!;
            if (!SafeIdentifier.IsMatch(filterColumn))
                throw new InvalidOperationException($"Nom de colonne de filtre invalide : {filterColumn}");
        }

        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand { Connection = con };

        if (filterColumn is not null && query is not null)
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

        cmd.CommandText = select.ToString();

        var rows = new List<MappedRow>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                dict[name] = reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i));
            }
            rows.Add(new MappedRow(dict));
        }
        return rows;
    }
}
