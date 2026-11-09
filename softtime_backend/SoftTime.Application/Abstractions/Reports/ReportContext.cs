using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Abstractions.Reports;

/// <summary>Données chargées une fois pour les états calculés (absences et retards).</summary>
public sealed record ReportContext(
    IReadOnlyList<CardPaie> Cartes,
    IReadOnlySet<DateTime> Feries,
    IReadOnlyDictionary<string, List<LeaveDay>> Conges,
    IReadOnlyList<HeureCorrigee> Corrections,
    IReadOnlyList<PlanningSalarie> Plannings,
    IReadOnlyDictionary<int, int> CategorieParCarte,
    IReadOnlyList<ToleranceRetard> Tolerances);
