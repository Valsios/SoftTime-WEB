using Microsoft.Data.SqlClient;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.Sage;

namespace SoftTime.Infrastructure.Integrations;

public class SagePayrollWriter : ISagePayrollWriter
{
    public async Task SyncOvertimeAsync(string sageConnectionString, T_CST[] constants, int numSal, OvertimeSageCodes codes, decimal exo130, decimal exo150, decimal i130, decimal i150, decimal ferie, decimal nuit, decimal dim, CancellationToken cancellationToken = default)
    {
        short Op(string code) => constants.First(c => c.CodeConstante.Trim() == code).CodeOperande1
            ?? throw new InvalidOperationException($"Constante SAGE {code} introuvable.");

        await UpdateCumAsync(sageConnectionString, numSal, Op(codes.Exo130), exo130, cancellationToken);
        await UpdateCumAsync(sageConnectionString, numSal, Op(codes.Exo150), exo150, cancellationToken);
        await UpdateCumAsync(sageConnectionString, numSal, Op(codes.I130), i130, cancellationToken);
        await UpdateCumAsync(sageConnectionString, numSal, Op(codes.I150), i150, cancellationToken);
        await UpdateCumAsync(sageConnectionString, numSal, Op(codes.Ferie), ferie, cancellationToken);
        await UpdateCumAsync(sageConnectionString, numSal, Op(codes.Dim), dim, cancellationToken);
        await UpdateCumAsync(sageConnectionString, numSal, Op(codes.Nuit), nuit, cancellationToken);
    }

    private static async Task UpdateCumAsync(string connectionString, int numSal, short opCst, decimal value, CancellationToken cancellationToken)
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
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
