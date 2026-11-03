using Microsoft.Extensions.Logging;
using SoftTime.Application.Abstractions;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

/// <summary>
/// Civilités SAGE par clé matricule (règle du 8 mars), partagées par l'écran HS et la base
/// analytique du moteur prédictif.
/// </summary>
internal static class SageCivilites
{
    public static async Task<Dictionary<string, byte?>> ChargerAsync(
        ITenantConnectionService tenant, IExternalSourceService externalSource, ILogger logger, CancellationToken ct)
    {
        var civilites = new Dictionary<string, byte?>(StringComparer.Ordinal);
        try
        {
            var sageRow = await tenant.GetSageRowAsync(ct);
            foreach (var employe in await externalSource.GetEmployeesAsync(sageRow, ct))
            {
                var cle = MatriculeIdentity.Cle(employe.Matricule);
                if (cle.Length > 0)
                    civilites[cle] = employe.Civilite;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // SAGE indisponible : sans civilité, la règle du 8 mars n'est pas appliquée ; la panne est journalisée.
            logger.LogWarning(ex, "Civilités SAGE indisponibles : la règle du 8 mars n'est pas appliquée.");
        }
        return civilites;
    }
}
