using SoftTime.Domain.Entities.SoftTime;
 
namespace SoftTime.Application.Abstractions;
 
public interface IPointeuseReader
{
    bool CanHandle(T_BDD_POINTEUSE pteRow);
 
    Task<IReadOnlyList<NormalizedPointeuseUser>> ListUsersAsync(T_BDD_POINTEUSE pteRow, CancellationToken ct);
 
    Task<IReadOnlyList<NormalizedPunch>> ListPunchesAsync(
        T_BDD_POINTEUSE pteRow, DateTime from, DateTime to, CancellationToken ct);
}