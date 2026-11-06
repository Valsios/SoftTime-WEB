using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
 
namespace SoftTime.Infrastructure.Readers;
 
public class StandardPointeuseReader : IPointeuseReader
{
    private readonly IPointeuseContextFactory _pointeuseFactory;
    public StandardPointeuseReader(IPointeuseContextFactory pointeuseFactory) => _pointeuseFactory = pointeuseFactory;
 
    public bool CanHandle(BddPointeuse pteRow) => pteRow.TypeBase == "STANDARD";
 
    public async Task<IReadOnlyList<NormalizedPointeuseUser>> ListUsersAsync(BddPointeuse pteRow, CancellationToken ct)
    {
        using var pte = _pointeuseFactory.Create(ExternalConnectionFactory.Build(
            SqlServerCredentials.Of(pteRow),
            pteRow.NomBd));
        return pte.Users.ToList()
            .Select(u => new NormalizedPointeuseUser(
                Id: u.UserId.ToString(),
                Badge: u.BadgeNumber,
                Ssn: u.Ssn,
                Nom: u.Name))
            .ToList();
    }
 
    public async Task<IReadOnlyList<NormalizedPunch>> ListPunchesAsync(
        BddPointeuse pteRow, DateTime from, DateTime to, CancellationToken ct)
    {
        using var pte = _pointeuseFactory.Create(ExternalConnectionFactory.Build(
            SqlServerCredentials.Of(pteRow),
            pteRow.NomBd));
        return pte.CheckInOut
            .Where(c => c.CheckTime >= from && c.CheckTime <= to)
            .ToList()
            .Select(c => new NormalizedPunch(
                UserId: c.UserId.ToString(),
                DateHeure: c.CheckTime,
                Type: c.CheckType))
            .ToList();
    }
}