using SoftTime.Domain.Entities.SoftTime;

 
namespace SoftTime.Application.Abstractions;
 
public interface IEmployeeReader
{
    bool CanHandle(BddSage sageRow);
 
    Task<IReadOnlyList<NormalizedEmployee>> ListActiveEmployeesAsync(BddSage sageRow, CancellationToken ct);
}