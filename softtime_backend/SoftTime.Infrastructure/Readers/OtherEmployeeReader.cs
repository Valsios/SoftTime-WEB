using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Readers;

public class OtherEmployeeReader : IEmployeeReader
{
    private static readonly Regex SafeIdentifier = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    public bool CanHandle(T_BDD_SAGE sageRow) => sageRow.TYPE_BASE == "AUTRE";

    public async Task<IReadOnlyList<NormalizedEmployee>> ListActiveEmployeesAsync(T_BDD_SAGE sageRow, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sageRow.MAP_TABLE) || string.IsNullOrWhiteSpace(sageRow.MAP_COL_MATRICULE))
            throw new InvalidOperationException("Mapping incomplet pour la base SAGE 'Autre' : table et colonne matricule requises.");

        foreach (var id in new[] { sageRow.MAP_TABLE, sageRow.MAP_COL_MATRICULE, sageRow.MAP_COL_NOM, sageRow.MAP_COL_PRENOM, sageRow.MAP_COL_BADGE })
            if (!string.IsNullOrWhiteSpace(id) && !SafeIdentifier.IsMatch(id))
                throw new InvalidOperationException($"Nom de table/colonne invalide dans le mapping SAGE 'Autre' : {id}");

        var selMatricule = $"[{sageRow.MAP_COL_MATRICULE}]";
        var selNom = sageRow.MAP_COL_NOM is { Length: > 0 } n ? $"[{n}]" : "NULL";
        var selPrenom = sageRow.MAP_COL_PRENOM is { Length: > 0 } p ? $"[{p}]" : "NULL";
        var selBadge = sageRow.MAP_COL_BADGE is { Length: > 0 } b ? $"[{b}]" : "NULL";

        var sql = $"SELECT {selMatricule} AS Matricule, {selNom} AS Nom, {selPrenom} AS Prenom, {selBadge} AS Badge " +
                  $"FROM [{sageRow.MAP_TABLE}]";

        var connStr = ExternalConnectionFactory.Build(sageRow.SERVEUR, sageRow.NOM_BD, sageRow.TYPE_AUTH == true, sageRow.TLOGIN, sageRow.TMDP);
        await using var con = new SqlConnection(connStr);
        await con.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, con);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var result = new List<NormalizedEmployee>();
        while (await reader.ReadAsync(ct))
        {
            var matricule = SqlReaderHelper.ReadText(reader, "Matricule");
            if (string.IsNullOrWhiteSpace(matricule))
                continue;
            result.Add(new NormalizedEmployee(
                Matricule: matricule,
                Nom: SqlReaderHelper.ReadText(reader, "Nom"),
                Prenom: SqlReaderHelper.ReadText(reader, "Prenom"),
                NumeroBadge: SqlReaderHelper.ReadText(reader, "Badge"),
                Desactive: false,
                CleInterne: null));
        }
        return result;
    }
}