using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services.Permissions;

public sealed class RightsChangeJournal : IRightsChangeJournal
{
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _currentUser;

    public RightsChangeJournal(IUnitOfWork uow, ICurrentUser currentUser)
    {
        _uow = uow;
        _currentUser = currentUser;
    }

    public async Task EcrireAuditDroitAsync(
        RightsChange change, CancellationToken cancellationToken)
    {
        var (idOperateur, loginOperateur, typeCible, idCible, nomCible, action, permsAvant, permsApres) = change;
        var avant = permsAvant == null ? null : string.Join(",", permsAvant);
        var apres = permsApres == null ? null : string.Join(",", permsApres);
        await _uow.Repository<AuditDroit>().AddAsync(new AuditDroit
        {
            DateAction = DateTime.UtcNow,
            IdOperateur = idOperateur,
            LoginOperateur = Truncate(loginOperateur, 100) ?? string.Empty,
            TypeCible = Truncate(typeCible, 20) ?? string.Empty,
            IdCible = idCible,
            NomCible = Truncate(nomCible, 100),
            Action = Truncate(action, 50) ?? string.Empty,
            PermsAvant = avant,
            PermsApres = apres
        }, cancellationToken);
    }

    public Task RecordAsync(RightsChangeEntry entry, CancellationToken cancellationToken)
        => EcrireAuditDroitAsync(
            new RightsChange(
                _currentUser.UserId ?? 0, _currentUser.Login ?? string.Empty,
                entry.TargetType, entry.TargetId, entry.TargetName, entry.Action, entry.Before, entry.After),
            cancellationToken);

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
