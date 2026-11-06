using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Services;

/// <summary>
/// Contexte partagé par la semaine, l'EXO/IMPO et le détail. Tous les chargements sont groupés
/// (une requête par table) : le calcul ne doit pas faire une requête par salarié.
/// </summary>
public sealed class WeeklyContext
{
    public string SageDb { get; init; } = string.Empty;
    public string PointeuseDb { get; init; } = string.Empty;
    public bool MultiPointage { get; init; } = true;
    public List<CardPaie> Cards { get; init; } = new();
    public List<JourFerie> Feries { get; init; } = new();
    public List<Categorie> Categories { get; init; } = new();
    public List<CategorieSalarie> Affectations { get; init; } = new();
    public List<ToleranceRetard> Tolerances { get; init; } = new();
    public List<Majoration> Majorations { get; init; } = new();
    public Dictionary<string, List<LeaveDay>> Conges { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<HeureCorrigee>> Heures { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<int, List<PlanningSalarie>> Planning { get; init; } = new();
    public Dictionary<string, HsValidee> Existantes { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, byte?> Civilites { get; init; } = new(StringComparer.Ordinal);
}
