namespace SoftTime.Application.Abstractions.Payroll;

/// <summary>Envoi en paie à enregistrer dans T_PAIE_ENVOI.</summary>
public sealed record PayrollSend(string SageDatabase, string TypeBase, DateTime PeriodFrom, DateTime PeriodTo, string? UserLogin, string Status, int Lines);

/// <summary>
/// Marqueur « période envoyée en paie » (T_PAIE_ENVOI), écrit par les writers indépendamment du journal central.
/// Chaque appel est sauvegardé immédiatement, hors de la transaction de la requête. En cas d'échec, l'erreur est
/// journalisée puis relancée : c'est au writer de décider s'il arrête l'envoi.
/// </summary>
public interface IPayrollSendLog
{
    /// <summary>Crée la ligne et renvoie son identifiant.</summary>
    Task<int> RecordAsync(PayrollSend send, CancellationToken ct = default);

    /// <summary>Met à jour le statut et le nombre de lignes d'un envoi déjà enregistré.</summary>
    Task CompleteAsync(int id, string status, int lines, CancellationToken ct = default);

    /// <summary>Supprime un envoi qui n'a finalement rien écrit (validation de la paie AUTRE annulée).</summary>
    Task RemoveAsync(int id, CancellationToken ct = default);

    /// <summary>Périodes envoyées d'une base.</summary>
    Task<IReadOnlyList<(DateTime Debut, DateTime Fin)>> ListPeriodsAsync(string sageDb, CancellationToken ct = default);
}
