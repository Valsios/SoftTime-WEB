using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Abstractions.Ml;

public sealed class DatasetContext
{
    public string SageDb { get; init; } = string.Empty;
    public string PointeuseDb { get; init; } = string.Empty;
    public DateTime From { get; init; }
    public DateTime To { get; init; }
    public DateTime WarmupFrom { get; init; }

    /// <summary>
    /// Premier jour non observé (aujourd'hui) : comme les états, aucune absence n'est constatée
    /// à partir de ce jour, et les historiques des jours futurs s'arrêtent à la veille.
    /// </summary>
    public DateTime ObservedUntil { get; init; }

    public HashSet<DateTime> JoursSansPointage { get; set; } = new();
    public bool MultiPointage { get; init; }
    public List<CardPaie> Cards { get; init; } = new();
    public List<Categorie> Categories { get; init; } = new();
    public List<CategorieSalarie> Affectations { get; init; } = new();
    public List<ToleranceRetard> Tolerances { get; init; } = new();
    public List<Majoration> Majorations { get; init; } = new();
    public HashSet<DateTime> Feries { get; init; } = new();
    public Dictionary<string, List<HeureCorrigee>> Corrections { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<int, List<PlanningSalarie>> PlanningHs { get; init; } = new();
    public Dictionary<string, List<PlanningSalarie>> PlanningEtats { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<int, int> CategorieParCarte { get; init; } = new();
    public Dictionary<string, List<LeaveDay>> Conges { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, byte?> Civilites { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<(string, DateTime), HsValidee> HsValidees { get; init; } = new();

    public Dictionary<string, CardPaie> CardByKey { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, EmployeeConfig> Configs { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<DayRow>> EmployeeDays { get; } = new(StringComparer.Ordinal);
    public Dictionary<(string, DateTime), WeekComputation> WeekResults { get; } = new();
}
