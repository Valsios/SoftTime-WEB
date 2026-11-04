using Microsoft.Data.SqlClient;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.Sage;

namespace SoftTime.Infrastructure.Integrations;

public class SagePayrollWriter : ISagePayrollWriter
{
    public async Task<IReadOnlyDictionary<string, int>> SyncOvertimeAsync(string sageConnectionString, T_CST[] constants, int numSal, OvertimeSageCodes codes, decimal exo130, decimal exo150, decimal i130, decimal i150, decimal ferie, decimal nuit, decimal dim, CancellationToken cancellationToken = default)
    {
        short Op(string code) => constants.FirstOrDefault(c => string.Equals(c.CodeConstante?.Trim(), code, StringComparison.Ordinal))?.CodeOperande1
            ?? throw new InvalidOperationException($"Constante SAGE {code} introuvable.");

        var updates = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            // Toutes les constantes sont résolues avant la première écriture : une constante absente
            // implique donc correctement zéro mise à jour pour ce salarié.
            var commands = new (string Category, short Operand, decimal Value)[]
            {
                ("Exo130", Op(codes.Exo130), exo130),
                ("Exo150", Op(codes.Exo150), exo150),
                ("I130", Op(codes.I130), i130),
                ("I150", Op(codes.I150), i150),
                ("Ferie", Op(codes.Ferie), ferie),
                ("Dimanche", Op(codes.Dim), dim),
                ("Nuit", Op(codes.Nuit), nuit)
            };
            foreach (var command in commands)
            {
                updates[command.Category] = await UpdateCumulAsync(
                    sageConnectionString, numSal, command.Operand, command.Value, cancellationToken);
            }
            return updates;
        }
        catch (Exception exception)
        {
            throw new PayrollSyncWriteException(exception, updates.Values.Sum(), updates);
        }
    }

    public virtual async Task<int> UpdateCumulAsync(string connectionString, int numSal, short opCst, decimal value, CancellationToken cancellationToken = default)
    {
        var formatted = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(
            "UPDATE T_CUMSAL SET ElementCumul = dbo.SetValeurCstBinaire(0,0,@val) WHERE NumSalarie=@num AND OpCstCumul=@op",
            con);
        cmd.Parameters.AddWithValue("@val", formatted);
        cmd.Parameters.AddWithValue("@num", numSal);
        cmd.Parameters.AddWithValue("@op", opCst);
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
