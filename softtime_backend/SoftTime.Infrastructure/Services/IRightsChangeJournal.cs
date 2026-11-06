namespace SoftTime.Infrastructure.Services;

public interface IRightsChangeJournal
{
    /// <summary>Ajoute une ligne au journal des droits (enregistrée avec la sauvegarde de l'appelant).</summary>
    Task EcrireAuditDroitAsync(RightsChange change, CancellationToken cancellationToken);
}
