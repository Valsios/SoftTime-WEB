using SoftTime.Application.DTOs;
using SoftTime.Domain.Models;

namespace SoftTime.Application.Abstractions;

public interface IPunchAggregate
{
    /// <summary>
    /// Jours distincts (matricule, date) pour lesquels un pointage tombe dans la fenêtre cantine.
    /// Le comptage du total cantine se fait ensuite en mémoire, et la même liste sert à la
    /// détection des anomalies.
    /// </summary>
    Task<IReadOnlyList<CanteenDay>> ListCanteenDaysAsync(
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
