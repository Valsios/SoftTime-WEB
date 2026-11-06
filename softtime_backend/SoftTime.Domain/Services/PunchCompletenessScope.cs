using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Domain.Services;

/// <summary>Données partagées par le contrôle des pointages incomplets de chaque salarié.</summary>
internal sealed record PunchCompletenessScope(
    ClosingPunchIndex Pointages,
    Dictionary<int, Dictionary<DateTime, PlanningSalarie>> PlanningParCarte,
    Func<string, DateTime, bool> EstIgnore,
    DateTime Debut,
    DateTime Fin,
    bool MultiPointage);
