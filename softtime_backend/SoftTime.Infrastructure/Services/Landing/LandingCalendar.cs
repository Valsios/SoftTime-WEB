using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Landing;

/// <summary>Journées de calcul HS d'un salarié, une par jour de la période.</summary>
internal static class LandingCalendar
{
    internal static IReadOnlyList<OvertimeDayInput> Jours(
        LandingPeriod period, IReadOnlySet<DateTime> feries, LandingEmployeeSources sources)
    {
        var conges = sources.Conges ?? new List<LeaveDay>();
        var corrections = (sources.Corrections ?? new List<HeureCorrigee>())
            .GroupBy(h => h.DatePointageIn!.Value.Date)
            .ToDictionary(g => g.Key, g => g.ToList());
        var plans = (sources.Plannings ?? new List<PlanningSalarie>())
            .GroupBy(p => p.DateP!.Value.Date)
            .ToDictionary(g => g.Key, g => OvertimeDayRules.PlanDuJour(g));

        var jours = new List<OvertimeDayInput>();
        for (var date = period.From; date <= period.To; date = date.AddDays(1))
        {
            jours.Add(OvertimeDayRules.Construire(
                new CalendarDay(date, feries.Contains(date), LeaveRules.Valeur(conges, date)),
                corrections.GetValueOrDefault(date) ?? new List<HeureCorrigee>(),
                plans.GetValueOrDefault(date)));
        }
        return jours;
    }
}
