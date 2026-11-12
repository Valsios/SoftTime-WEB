namespace SoftTime.Application.Abstractions.Permissions;

public interface IRightsChangeJournal
{
    /// <summary>Ajoute une ligne au journal des droits (enregistrée avec la sauvegarde de l'appelant).</summary>
    Task EcrireAuditDroitAsync(RightsChange change, CancellationToken cancellationToken);

    /// <summary>Comme <see cref="EcrireAuditDroitAsync"/>, l'opérateur étant le compte connecté.</summary>
    Task RecordAsync(RightsChangeEntry entry, CancellationToken cancellationToken);
}
