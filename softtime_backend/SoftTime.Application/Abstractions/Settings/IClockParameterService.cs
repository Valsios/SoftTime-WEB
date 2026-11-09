using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.Settings;

public interface IClockParameterService
{
    Task<ClockParamDto> GetClockAsync(CancellationToken ct = default);

    Task<ClockParamDto> SaveClockAsync(ClockParamDto dto, CancellationToken ct = default);
}
