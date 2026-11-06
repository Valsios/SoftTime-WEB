namespace SoftTime.Infrastructure.Services;

public interface IRightsChangeJournal
{
    Task EcrireAuditDroitAsync(decimal idOperateur, string loginOperateur, string typeCible, decimal idCible, string? nomCible, string action, IEnumerable<string>? permsAvant, IEnumerable<string>? permsApres, CancellationToken cancellationToken);
}
