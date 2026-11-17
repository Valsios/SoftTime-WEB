using Microsoft.Data.SqlClient;

using SoftTime.Application.Abstractions.Payroll;

namespace SoftTime.Infrastructure.Integrations;

public class SagePayrollWriter : ISagePayrollWriter
{
    /// <summary>Commande historique d'écriture d'un cumul (inchangée).</summary>
    internal const string UpdateCumulSql =
        "UPDATE T_CUMSAL SET ElementCumul = dbo.SetValeurCstBinaire(0,0,@val) WHERE NumSalarie=@num AND OpCstCumul=@op";

    public async Task<IReadOnlyDictionary<string, int>> SyncOvertimeAsync(SagePayrollTarget target, int numSal, OvertimeAmounts amounts, CancellationToken cancellationToken = default)
    {
        var (sageConnectionString, constants, _) = target;
        short Op(string code) => constants.FirstOrDefault(c => string.Equals(c.CodeConstante?.Trim(), code, StringComparison.Ordinal))?.CodeOperande1
            ?? throw new DomainException($"Constante SAGE {code} introuvable.");

        var updates = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            // Toutes les constantes des variables envoyées sont résolues avant la première écriture : une constante
            // absente implique donc correctement zéro mise à jour pour ce salarié. Une variable décochée n'est ni
            // résolue ni écrite : la valeur existante de T_CUMSAL n'est pas touchée. Ordre : celui du catalogue
            // (EXO 130, EXO 150, IMPO 130, IMPO 150, Férié, Dimanche, Nuit pour les heures supplémentaires). Valeur : celle
            // affichée, en heures, convertie en jours pour une variable envoyée en jours (Absence, lot C3).
            var commands = target.Variables.Envoyees
                .Select(variable => (Category: variable, Operand: Op(target.CodeDe(variable)), Value: target.Variables.ValeurEnvoyee(variable, amounts.Valeurs)))
                .ToList();
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
        await using var cmd = new SqlCommand(UpdateCumulSql, con);
        cmd.Parameters.AddWithValue("@val", formatted);
        cmd.Parameters.AddWithValue("@num", numSal);
        cmd.Parameters.AddWithValue("@op", opCst);
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
