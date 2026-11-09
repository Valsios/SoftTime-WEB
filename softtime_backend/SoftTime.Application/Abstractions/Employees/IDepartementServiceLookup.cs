using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.Employees;

public interface IDepartementServiceLookup
{
    Task<DepartementServiceDto> GetDepartementServiceAsync(string matricule, CancellationToken ct = default);

    Task<IReadOnlyList<DepartementServiceDto>> GetDepartementServiceBatchAsync(IEnumerable<string> matricules, CancellationToken ct = default);
}
