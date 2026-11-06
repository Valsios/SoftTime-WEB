using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface IPayrollWriteConfigurationService
{
    Task<IReadOnlyList<PayrollWriteConfigurationDto>> ListPayrollWriteConfigurationsAsync(CancellationToken ct = default);

    Task<PayrollWriteConfigurationDto> SavePayrollWriteConfigurationAsync(PayrollWriteConfigurationDto dto, CancellationToken ct = default);

    Task DeletePayrollWriteConfigurationAsync(int id, CancellationToken ct = default);

    Task<ConnectionTestResultDto> TestPayrollWriteConfigurationAsync(CancellationToken ct = default);
}
