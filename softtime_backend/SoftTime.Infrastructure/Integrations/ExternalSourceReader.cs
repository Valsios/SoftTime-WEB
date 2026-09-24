using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;

namespace SoftTime.Infrastructure.Integrations;

public class ExternalSourceReader : IExternalSourceReader
{
    private static readonly Regex SafeIdentifier = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    public async Task<DepartementServiceDto> GetDepartementServiceAsync(
        string connectionString,
        SourceConfigDto config,
        string matricule,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(config.TableName) || string.IsNullOrWhiteSpace(config.ColMatricule))
            throw new InvalidOperationException("Configuration 'Autre base' incomplète : table et colonne matricule requises.");

        foreach (var id in new[] { config.TableName, config.ColMatricule, config.ColDepartement, config.ColService })
            if (!string.IsNullOrWhiteSpace(id) && !SafeIdentifier.IsMatch(id))
                throw new InvalidOperationException($"Nom de table/colonne invalide dans la configuration : {id}");

        var selectDep = string.IsNullOrWhiteSpace(config.ColDepartement) ? "NULL" : $"[{config.ColDepartement}]";
        var selectServ = string.IsNullOrWhiteSpace(config.ColService) ? "NULL" : $"[{config.ColService}]";
        var sql = $"SELECT TOP 1 {selectDep} AS Departement, {selectServ} AS Service " +
          $"FROM [{config.TableName}] WHERE CAST([{config.ColMatricule}] AS NVARCHAR(64)) = @matricule";

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