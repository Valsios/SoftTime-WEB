using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Services.Dashboard;

/// <summary>Données chargées une fois pour calculer les statistiques du tableau de bord.</summary>
internal sealed class DashboardData
{
    public required DashboardPerimetre Perimetre { get; init; }

    public required DateTime From { get; init; }

    public required DateTime To { get; init; }

    /// <summary>Dernier jour pris en compte : aucun événement futur, au plus tard la veille.</summary>
    public required DateTime FinEvenements { get; init; }

    /// <summary>Carte de référence par clé de matricule.</summary>
    public required Dictionary<string, CardPaie> Cles { get; init; }

    public required Dictionary<string, string?> CategorieParCle { get; init; }

    public required HashSet<DateTime> Feries { get; init; }

    public required Dictionary<string, List<LeaveDay>> CongesParCle { get; init; }

    public required Dictionary<string, List<HeureCorrigee>> CorrectionsParCle { get; init; }

    /// <summary>Planning par identifiant de carte puis par jour.</summary>
    public required Dictionary<int, Dictionary<DateTime, PlanningSalarie>> Planning { get; init; }

    public required List<ToleranceRetard> Tolerances { get; init; }

    public required List<SemaineHs> Semaines { get; init; }

    public required Dictionary<string, decimal> HsParCle { get; init; }

    /// <summary>Jour exclu des événements : futur, week-end ou férié.</summary>
    public bool EstExclu(DateTime jour) =>
        jour > FinEvenements || DashboardIndicators.EstNonTravaille(jour, Feries);
}
