using SoftTime.Domain.Entities.SoftTime;

 
namespace SoftTime.Application.Abstractions;
 
public interface IPointeuseReader
{
    bool CanHandle(BddPointeuse pteRow);
 
    Task<IReadOnlyList<NormalizedPointeuseUser>> ListUsersAsync(BddPointeuse pteRow, CancellationToken ct);
 
    Task<IReadOnlyList<NormalizedPunch>> ListPunchesAsync(
        BddPointeuse pteRow, DateTime from, DateTime to, CancellationToken ct);
}