using System.Text.RegularExpressions;

using Microsoft.Data.SqlClient;

using SoftTime.Application.Abstractions.DataSources;
using SoftTime.Application.Abstractions.ExternalData;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Readers;

public class OtherEmployeeReader : IEmployeeReader
{
    private static readonly Regex SafeIdentifier = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    public bool CanHandle(BddSage sageRow) => sageRow.TypeBase == "AUTRE";

    public async Task<IReadOnlyList<NormalizedEmployee>> ListActiveEmployeesAsync(BddSage sageRow, CancellationToken ct)
    {
        var sql = Requete(sageRow);

        var connStr = ExternalConnectionFactory.Build(SqlServerCredentials.Of(sageRow), sageRow.NomBd);
        await using var con = new SqlConnection(connStr);
        await con.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, con);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var result = new List<NormalizedEmployee>();
        while (await reader.ReadAsync(ct))
        {
            var matricule = SqlColumnReader.ReadText(reader, "Matricule");
            if (string.IsNullOrWhiteSpace(matricule))
                continue;
            result.Add(new NormalizedEmployee(
                Matricule: matricule,
                Nom: SqlColumnReader.ReadText(reader, "Nom"),
                Prenom: SqlColumnReader.ReadText(reader, "Prenom"),
                NumeroBadge: SqlColumnReader.ReadText(reader, "Badge"),
                Desactive: false,
                CleInterne: null));
        }
        return result;
    }

    /// <summary>Requête de lecture des salariés d'après le mapping de la base, identifiants SQL contrôlés.</summary>
    private static string Requete(BddSage sageRow)
    {
        if (string.IsNullOrWhiteSpace(sageRow.MapTable) || string.IsNullOrWhiteSpace(sageRow.MapColMatricule))
            throw new DomainException("Mapping incomplet pour la base SAGE 'Autre' : table et colonne matricule requises.");

        foreach (var id in new[] { sageRow.MapTable, sageRow.MapColMatricule, sageRow.MapColNom, sageRow.MapColPrenom, sageRow.MapColBadge })
            if (!string.IsNullOrWhiteSpace(id) && !SafeIdentifier.IsMatch(id))
                throw new DomainException($"Nom de table/colonne invalide dans le mapping SAGE 'Autre' : {id}");

        var selMatricule = $"[{sageRow.MapColMatricule}]";
        var selNom = sageRow.MapColNom is { Length: > 0 } n ? $"[{n}]" : "NULL";
        var selPrenom = sageRow.MapColPrenom is { Length: > 0 } p ? $"[{p}]" : "NULL";
        var selBadge = sageRow.MapColBadge is { Length: > 0 } b ? $"[{b}]" : "NULL";

        var sql = $"SELECT {selMatricule} AS Matricule, {selNom} AS Nom, {selPrenom} AS Prenom, {selBadge} AS Badge " +
                  $"FROM [{sageRow.MapTable}]";
        return sql;
    }
}