using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.Sage;

namespace SoftTime.Application.Abstractions;

public interface ISagePayrollWriter
{
    Task SyncOvertimeAsync(string sageConnectionString, T_CST[] constants, int numSal, OvertimeSageCodes codes, decimal exo130, decimal exo150, decimal i130, decimal i150, decimal ferie, decimal nuit, decimal dim, CancellationToken cancellationToken = default);
}
