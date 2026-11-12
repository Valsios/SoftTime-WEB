using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Domain.Services;

/// <summary>
/// Un salarié est-il concerné par une semaine ? Sert à masquer, dans la validation semaine, les
/// salariés qui n'ont ni heures corrigées ni jour planifié sur la période.
/// </summary>
public static class WeeklyActivityRules
{
    /// <summary>
    /// Vrai si le salarié a au moins une heure corrigée sur la semaine, ou au moins un jour
    /// planifié travaillé (un jour <c>OFF</c> ne compte pas). Un salarié planifié mais absent reste
    /// donc visible : son absence doit être validée comme le reste de la semaine.
    /// </summary>
    public static bool ASemaineActive(
        IReadOnlyCollection<HeureCorrigee> heures,
        IReadOnlyCollection<PlanningSalarie> planning)
        => heures.Count > 0 || planning.Any(plan => plan.OffP != true);
}
