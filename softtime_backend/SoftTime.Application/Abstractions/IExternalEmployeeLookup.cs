using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Abstractions;

public interface IExternalEmployeeLookup
{
    Task<DepartementServiceDto> GetDepartementServiceAsync(
        string connectionString,
        T_BDD_SAGE database,
        string matricule,
        CancellationToken cancellationToken = default);
}
