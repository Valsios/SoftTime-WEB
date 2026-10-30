using Microsoft.Data.SqlClient;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.Sage;

namespace SoftTime.Infrastructure.Integrations;

public class SagePayrollWriter : ISagePayrollWriter
{
    public async Task<bool> SyncOvertimeAsync(string sageConnectionString, T_CST[] constants, int numSal, OvertimeSageCodes codes, decimal exo130, decimal exo150, decimal i130, decimal i150, decimal ferie, decimal nuit, decimal dim, CancellationToken cancellationToken = default)
    {
        short Op(string code) => constants.First(c => c.CodeConstante?.Trim() == code).CodeOperande1
            ?? throw new InvalidOperationException($"Constante SAGE {code} introuvable.");

        var cumuls = new (short Op, decimal Valeur)[]
        {
            (Op(codes.Exo130), exo130), (Op(codes.Exo150), exo150), (Op(codes.I130), i130), (Op(codes.I150), i150),
            (Op(codes.Ferie), ferie), (Op(codes.Dim), dim), (Op(codes.Nuit), nuit)
        };

        // Les sept cumuls d'un salarié sont écrits ensemble : un cumul absent annule les autres, pour
        // ne jamais laisser un salarié à moitié synchronisé.
        await using var con = new SqlConnection(sageConnectionString);
        await con.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await con.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var (op, valeur) in cumuls)
            {
                if (await ExecuteUpdateAsync(con, transaction, numSal, op, valeur, cancellationToken) == 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return false;
                }
            }
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<int> UpdateCumulAsync(string connectionString, int numSal, short opCst, decimal value, CancellationToken cancellationToken = default)
    {
        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync(cancellationToken);
        return await ExecuteUpdateAsync(con, null, numSal, opCst, value, cancellationToken);
    }

    private static async Task<int> ExecuteUpdateAsync(SqlConnection con, SqlTransaction? transaction, int numSal, short opCst, decimal value, CancellationToken cancellationToken)
    {
        var formatted = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await using var cmd = new SqlCommand(
            "UPDATE T_CUMSAL SET ElementCumul = dbo.SetValeurCstBinaire(0,0,@val) WHERE NumSalarie=@num AND OpCstCumul=@op",
            con, transaction);
        cmd.Parameters.AddWithValue("@val", formatted);
        cmd.Parameters.AddWithValue("@num", numSal);
        cmd.Parameters.AddWithValue("@op", opCst);
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
