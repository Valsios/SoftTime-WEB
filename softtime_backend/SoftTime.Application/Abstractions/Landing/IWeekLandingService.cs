using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.Landing;

/// <summary>
/// Atterrissage des heures supplémentaires : ce que chaque salarié aura fait en fin de semaine,
/// d'après les pointages déjà traités et le planning des jours restants. Lecture seule.
/// </summary>
public interface IWeekLandingService
{
    /// <summary>Atterrissage par salarié de la semaine contenant <paramref name="semaine"/>.</summary>
    Task<WeekLandingDto> GetAsync(DateTime semaine, CancellationToken ct = default);

    /// <summary>Totaux de la semaine en cours et des semaines suivantes déjà planifiées.</summary>
    Task<IReadOnlyList<WeekLandingWeekDto>> GetHorizonAsync(CancellationToken ct = default);
}
