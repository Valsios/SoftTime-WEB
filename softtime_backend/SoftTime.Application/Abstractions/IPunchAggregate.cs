using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface IPunchAggregate
{
    Task<IReadOnlyList<PunchCountDto>> CountCanteenDaysAsync(
        string sageDb,
        DateTime from,
        DateTime toExclusive,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PunchCountDto>> CountTravelPunchesAsync(
        string sageDb,
        DateTime from,
        DateTime toExclusive,
        CancellationToken cancellationToken = default);
}
