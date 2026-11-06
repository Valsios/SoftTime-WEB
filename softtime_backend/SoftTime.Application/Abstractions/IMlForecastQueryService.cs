using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface IMlForecastQueryService
{
    Task<MlStatusDto> GetStatusAsync(CancellationToken ct = default);

    Task<IReadOnlyList<MlHsForecastDto>> GetHsForecastsAsync(DateTime semaine, CancellationToken ct = default);

    /// <summary>
    /// Totaux HS par semaine sur l'horizon (semaine en cours comprise). Les semaines sans
    /// prévision (planning pas encore saisi) sont renvoyées à zéro pour que l'écran l'explique.
    /// </summary>
    Task<IReadOnlyList<MlHsWeekDto>> GetHsHorizonAsync(CancellationToken ct = default);

    /// <summary>
    /// Prévu / réel des dernières semaines terminées : la dernière prévision émise pour chaque
    /// salarié est comparée aux HS de la base analytique (validées, sinon calculées). Seuls les
    /// salariés présents des deux côtés sont comparés.
    /// </summary>
    Task<IReadOnlyList<MlHsAccuracyDto>> GetHsAccuracyAsync(int semaines, CancellationToken ct = default);
}
