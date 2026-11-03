using SoftTime.Application.DTOs;
using SoftTime.Application.Services;

namespace SoftTime.Application.Services.Interfaces;

/// <summary>Cas d'usage de l'écran Prévisions : lecture contrôlée et déclenchement des traitements du moteur prédictif.</summary>
public interface IPredictionsService
{
    Task<MlStatusDto> GetStatusAsync(CancellationToken ct = default);

    Task<MlJobState> GetJobAsync(CancellationToken ct = default);

    /// <summary>Prévisions d'absence ; sans dates : 14 jours à partir d'aujourd'hui.</summary>
    Task<IReadOnlyList<MlAbsenceForecastDto>> GetAbsenceAsync(DateTime? from, DateTime? to, string? departement, int? categorie, CancellationToken ct = default);

    /// <summary>Prévisions d'heures supplémentaires de la semaine (semaine courante sans date).</summary>
    Task<IReadOnlyList<MlHsForecastDto>> GetOvertimeAsync(DateTime? week, CancellationToken ct = default);

    /// <summary>Programme un traitement (administrateur) et renvoie le message de confirmation ; <see cref="ConflictException"/> si un traitement tourne déjà.</summary>
    string Schedule(PredictionJob job);
}

/// <summary>Traitements déclenchables depuis l'écran Prévisions.</summary>
public enum PredictionJob
{
    Refresh,
    Backfill,
    Train
}
