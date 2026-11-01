namespace SoftTime.Application.Services;

/// <summary>
/// Détection des trous de données de pointage, partagée par la construction de la base analytique
/// (features) et l'entraînement (labels). Le label d'absence reste celui de l'état des absences ;
/// ces jours sont seulement écartés de l'apprentissage et des historiques glissants.
/// </summary>
internal static class MlDataQuality
{
    public static HashSet<DateTime> JoursSansPointage(
        IEnumerable<(DateTime Date, bool Planifie, bool Absent)> jours, MlOptions options)
        => jours
            .GroupBy(j => j.Date.Date)
            .Where(g =>
            {
                var planifies = g.Count(j => j.Planifie);
                if (planifies < Math.Max(1, options.MissingPunchDayMinPlanned))
                    return false;
                var absents = g.Count(j => j.Planifie && j.Absent);
                return (double)absents / planifies >= options.MissingPunchDayAbsenceRate;
            })
            .Select(g => g.Key)
            .ToHashSet();
}
