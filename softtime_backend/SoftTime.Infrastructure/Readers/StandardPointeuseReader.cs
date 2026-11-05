using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
 
namespace SoftTime.Infrastructure.Readers;
 
public class StandardPointeuseReader : IPointeuseReader
{
    private readonly IPointeuseContextFactory _pointeuseFactory;
    public StandardPointeuseReader(IPointeuseContextFactory pointeuseFactory) => _pointeuseFactory = pointeuseFactory;
 
    public bool CanHandle(T_BDD_POINTEUSE pteRow) => pteRow.TYPE_BASE == "STANDARD";
 
    public async Task<IReadOnlyList<NormalizedPointeuseUser>> ListUsersAsync(T_BDD_POINTEUSE pteRow, CancellationToken ct)
    {
        using var pte = _pointeuseFactory.Create(ExternalConnectionFactory.Build(
            pteRow.SERVEUR, pteRow.NOM_BD, pteRow.TYPE_AUTH == true, pteRow.TLOGIN, pteRow.TMDP));
        return pte.Users.ToList()
            .Select(u => new NormalizedPointeuseUser(
                Id: u.USERID.ToString(),
                Badge: u.BADGENUMBER,
                Ssn: u.SSN,
                Nom: u.NAME))
            .ToList();
    }
 
    public async Task<IReadOnlyList<NormalizedPunch>> ListPunchesAsync(
        T_BDD_POINTEUSE pteRow, DateTime from, DateTime to, CancellationToken ct)
    {
        using var pte = _pointeuseFactory.Create(ExternalConnectionFactory.Build(
            pteRow.SERVEUR, pteRow.NOM_BD, pteRow.TYPE_AUTH == true, pteRow.TLOGIN, pteRow.TMDP));
        return pte.CheckInOut
            .Where(c => c.CHECKTIME >= from && c.CHECKTIME <= to)
            .ToList()
            .Select(c => new NormalizedPunch(
                UserId: c.USERID.ToString(),
                DateHeure: c.CHECKTIME,
                Type: c.CHECKTYPE))
            .ToList();
    }
}