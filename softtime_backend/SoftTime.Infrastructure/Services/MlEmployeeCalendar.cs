using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

/// <summary>
/// Journées d'un salarié pour le jeu de données ML : congés, corrections et deux vues du planning,
/// chacune alignée sur l'écran qui l'utilise — états absences/retards (toutes cartes de la clé,
/// travail prioritaire sur OFF) et écran HS (carte et pointeuse actives, plus petit ID).
/// </summary>
internal sealed class MlEmployeeCalendar
{
    private readonly DatasetContext _ctx;
    private readonly CardPaie _card;
    private readonly string _cle;
    private readonly List<LeaveDay> _conges;
    private readonly Dictionary<DateTime, List<HeureCorrigee>> _correctionsParJour;
    private readonly Dictionary<DateTime, PlanningSalarie?> _plansParJour;
    private readonly Dictionary<DateTime, PlanningSalarie?> _plansHsParJour;
    private readonly int _toleranceEntree;

    public MlEmployeeCalendar(DatasetContext ctx, CardPaie card, string cle)
    {
        _ctx = ctx;
        _card = card;
        _cle = cle;
        _conges = ctx.Conges.GetValueOrDefault(cle) ?? new List<LeaveDay>();
        _correctionsParJour = (ctx.Corrections.GetValueOrDefault(cle) ?? new List<HeureCorrigee>())
            .Where(h => h.DatePointageIn.HasValue)
            .GroupBy(h => h.DatePointageIn!.Value.Date)
            .ToDictionary(g => g.Key, g => g.ToList());
        _plansParJour = (ctx.PlanningEtats.GetValueOrDefault(cle) ?? new List<PlanningSalarie>())
            .GroupBy(p => p.DateP!.Value.Date)
            .ToDictionary(g => g.Key, g => MlDayFeatures.SelectPlan(g));
        _plansHsParJour = (ctx.PlanningHs.GetValueOrDefault(card.Id) ?? new List<PlanningSalarie>())
            .GroupBy(p => p.DateP!.Value.Date)
            .ToDictionary(g => g.Key, g => OvertimeDayRules.PlanDuJour(g));
        _toleranceEntree = RetardReportRules.ToleranceEntree(card.Id, ctx.CategorieParCarte, ctx.Tolerances);
    }

    /// <summary>Une ligne par jour, de la période de chauffe jusqu'à la fin du jeu de données.</summary>
    public List<DayRow> Days()
    {
        var days = new List<DayRow>();
        for (var date = _ctx.WarmupFrom; date <= _ctx.To; date = date.AddDays(1))
            days.Add(BuildDay(date));
        return days;
    }

    private DayRow BuildDay(DateTime date)
    {
        var plan = _plansParJour.GetValueOrDefault(date);
        var lignes = _correctionsParJour.GetValueOrDefault(date) ?? new List<HeureCorrigee>();
        var off = AbsenceReportRules.EstJourOff(plan);
        var ferie = _ctx.Feries.Contains(date);
        var weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var congeValeur = LeaveRules.Valeur(_conges, date);
        var conge = congeValeur > 0m;
        var dureePlanifiee = off ? null : AbsenceReportRules.CalculerDureeHeures(plan);
        var overtimeDay = OvertimeDayRules.Construire(date, lignes, _plansHsParJour.GetValueOrDefault(date), ferie, congeValeur);
        var absenceNonJustifiee = date < _ctx.ObservedUntil
            && AbsenceReportRules.EstNonJustifiee(estNonTravaille: weekend || ferie, estConge: conge, plan, aPointage: lignes.Count > 0);

        return new DayRow
        {
            Matricule = _cle,
            Date = date,
            Departement = _card.Departement,
            Off = off,
            Ferie = ferie,
            Weekend = weekend,
            Conge = conge,
            CongeValeur = congeValeur,
            PlanningConnu = plan != null,
            EstJourPlanifie = !off && !weekend && !ferie && !conge && dureePlanifiee.HasValue,
            HeuresPlanifiees = dureePlanifiee,
            HeuresTravaillees = MlDayFeatures.HeuresTravaillees(overtimeDay.Paires),
            RetardMinutes = weekend || ferie || lignes.Count == 0 ? 0 : RetardMinutes(lignes, plan),
            AbsenceNonJustifiee = absenceNonJustifiee,
            AbsenceDeclaree = lignes.Any(h => h.AbsenceAm == true || h.AbsencePm == true),
            AbsenceHeures = absenceNonJustifiee ? dureePlanifiee : null,
            OvertimeDay = overtimeDay
        };
    }

    /// <summary>
    /// Même règle que l'état des retards et le tableau de bord : valeur saisie prioritaire, sinon
    /// première entrée du jour vs heure planifiée + tolérance de la catégorie.
    /// </summary>
    private int RetardMinutes(List<HeureCorrigee> lignes, PlanningSalarie? plan)
    {
        var entrees = lignes.Where(h => h.HEntree.HasValue).Select(h => h.HEntree!.Value).ToList();
        return RetardReportRules.MinutesRetardJour(
            absenceMatin: lignes.Any(h => h.AbsenceAm == true),
            retardsSaisis: lignes.Select(h => h.Retard),
            premiereEntree: entrees.Count > 0 ? entrees.Min() : null,
            heurePrevue: RetardReportRules.HeurePrevue(plan),
            toleranceMinutes: _toleranceEntree);
    }
}
