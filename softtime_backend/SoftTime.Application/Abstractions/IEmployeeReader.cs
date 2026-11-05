using SoftTime.Domain.Entities.SoftTime;
 
namespace SoftTime.Application.Abstractions;
 
public interface IEmployeeReader
{
    bool CanHandle(T_BDD_SAGE sageRow);
 
    Task<IReadOnlyList<NormalizedEmployee>> ListActiveEmployeesAsync(T_BDD_SAGE sageRow, CancellationToken ct);
}