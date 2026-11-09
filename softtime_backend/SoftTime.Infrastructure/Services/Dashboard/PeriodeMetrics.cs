namespace SoftTime.Infrastructure.Services.Dashboard;

/// <summary>Compteurs de la période, calculés sur les cartes filtrées par la page.</summary>
public sealed record PeriodeMetrics(
    int JoursPlanifies,
    int JoursTravailles,
    int JoursAbsence,
    int JoursRetard,
    int MinutesRetard,
    IReadOnlyList<SemaineHs> Semaines);
