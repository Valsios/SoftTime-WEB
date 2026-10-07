using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Integrations;

public class ExternalEmployeeLookup : IExternalEmployeeLookup
{
    private static readonly Regex SafeIdentifier = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    public async Task<DepartementServiceDto> GetDepartementServiceAsync(
        string connectionString,
        T_BDD_SAGE database,
        string matricule,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(database.MAP_TABLE) || string.IsNullOrWhiteSpace(database.MAP_COL_MATRICULE))
            throw new InvalidOperationException("Configuration de la base RH/paie AUTRE incomplète : table et colonne matricule requises.");

        foreach (var id in new[] { database.MAP_TABLE, database.MAP_COL_MATRICULE, database.MAP_COL_DEPARTEMENT, database.MAP_COL_SERVICE })
            if (!string.IsNullOrWhiteSpace(id) && !SafeIdentifier.IsMatch(id))
                throw new InvalidOperationException($"Nom de table/colonne invalide dans la configuration RH/paie : {id}");

        var selectDep = string.IsNullOrWhiteSpace(database.MAP_COL_DEPARTEMENT) ? "NULL" : $"[{database.MAP_COL_DEPARTEMENT}]";
        var selectServ = string.IsNullOrWhiteSpace(database.MAP_COL_SERVICE) ? "NULL" : $"[{database.MAP_COL_SERVICE}]";
        var sql = $"SELECT TOP 1 {selectDep} AS Departement, {selectServ} AS Service " +
            $"FROM [{database.MAP_TABLE}] WHERE CAST([{database.MAP_COL_MATRICULE}] AS NVARCHAR(64)) = @matricule";

        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.AddWithValue("@matricule", matricule.Trim());
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            var dep = reader.IsDBNull(0) ? null : Convert.ToString(reader.GetValue(0))?.Trim();
            var serv = reader.IsDBNull(1) ? null : Convert.ToString(reader.GetValue(1))?.Trim();
            return new DepartementServiceDto(matricule, dep, serv);
        }

        return new DepartementServiceDto(matricule, null, null);
    }
}
