using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Abstractions.RequestContext;

namespace SoftTime.Infrastructure.Services.Permissions;

/// <summary>Écrit dans le journal des droits avec l'identité du compte de la requête (identifiant et login du jeton).</summary>
public sealed class CurrentOperatorRightsJournal : ICurrentOperatorRightsJournal
{
    private readonly IRightsChangeJournal _journal;
    private readonly ICurrentUser _user;

    public CurrentOperatorRightsJournal(IRightsChangeJournal journal, ICurrentUser user)
    {
        _journal = journal;
        _user = user;
    }

    public Task RecordAsync(
        RightsTarget target, string action, IEnumerable<string>? before, IEnumerable<string>? after, CancellationToken ct)
        => _journal.EcrireAuditDroitAsync(
            new RightsChange(_user.UserId ?? 0, _user.Login ?? string.Empty, target.Type, target.Id, target.Name, action, before, after),
            ct);
}
