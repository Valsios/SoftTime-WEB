using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Abstractions.Reports;

/// <summary>
/// Données chargées une fois pour les états calculés (absences et retards).
/// <paramref name="CongesDisponibles"/> : faux si les congés SAGE n'ont pas pu être lus (la liste des congés est alors
/// vide) ; les états qui ne peuvent pas s'en passer doivent le signaler plutôt que d'afficher un résultat faux.
/// </summary>
public sealed record ReportContext(
    IReadOnlyList<CardPaie> Cartes,
    IReadOnlySet<DateTime> Feries,
    IReadOnlyDictionary<string, List<LeaveDay>> Conges,
    IReadOnlyList<HeureCorrigee> Corrections,
    IReadOnlyList<PlanningSalarie> Plannings,
    IReadOnlyDictionary<int, int> CategorieParCarte,
    IReadOnlyList<ToleranceRetard> Tolerances,
    bool CongesDisponibles = true);
