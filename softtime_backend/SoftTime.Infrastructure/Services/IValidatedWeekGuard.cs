namespace SoftTime.Infrastructure.Services;

public interface IValidatedWeekGuard
{
    /// <summary>Refuse toute modification rétroactive sur une semaine déjà validée.</summary>
    Task RefuserSiSemaineValideeAsync(string sageDb, string? matricule, DateTime? jour, CancellationToken ct);

    /// <summary>
    /// Semaines validées de la base active, par (clé matricule, lundi) : chargées une fois pour les
    /// opérations de masse (import, validation d'anomalies) au lieu d'une requête par ligne.
    /// </summary>
    Task<HashSet<(string Cle, DateTime Lundi)>> ChargerSemainesValideesAsync(string sageDb, CancellationToken ct);
}
