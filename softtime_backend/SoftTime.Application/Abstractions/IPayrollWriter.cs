using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Abstractions;

public interface IPayrollWriter
{
    bool CanHandle(T_BDD_SAGE database);
    Task<PayrollWriteResultDto> ValidateAsync(T_BDD_SAGE database, IReadOnlyList<HsExoDto> rows, CancellationToken ct = default);
    Task<PayrollWriteResultDto> WriteAsync(T_BDD_SAGE database, IReadOnlyList<HsExoDto> rows, CancellationToken ct = default);
}
