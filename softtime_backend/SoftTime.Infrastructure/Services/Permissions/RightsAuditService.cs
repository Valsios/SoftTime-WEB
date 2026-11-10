using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services.Permissions;

public sealed class RightsAuditService : IRightsAuditService
{
    private readonly IUnitOfWork _uow;

    public RightsAuditService(
        IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<IReadOnlyList<AuditDroitDto>> GetAuditDroitsAsync(
        string? typeCible, decimal? idCible, DateTime? from, DateTime? to,
        CancellationToken cancellationToken = default)
    {
        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow.AddDays(1);
        var list = await _uow.Repository<AuditDroit>().ListLatestAsync(
            a => a.DateAction >= fromDate
                 && a.DateAction <= toDate
                 && (string.IsNullOrEmpty(typeCible) || a.TypeCible == typeCible)
                 && (!idCible.HasValue || a.IdCible == idCible.Value),
            a => (DateTime?)a.DateAction,
            take: 500,
            cancellationToken);

        return list.Select(a => new AuditDroitDto(
            a.Id, a.DateAction, a.IdOperateur, a.LoginOperateur,
            a.TypeCible, a.IdCible, a.NomCible,
            a.Action, a.PermsAvant, a.PermsApres)).ToList();
    }
}
