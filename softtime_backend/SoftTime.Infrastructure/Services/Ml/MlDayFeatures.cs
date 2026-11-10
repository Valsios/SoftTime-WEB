using SoftTime.Application.Abstractions.Ml;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Ml;

internal static class MlDayFeatures
{

    internal static EmployeeConfig BuildConfig(DatasetContext ctx, CardPaie card)
    {
        var affectation = ctx.Affectations.FirstOrDefault(a => a.MatriculeSage == card.Id);
        var categorie = affectation == null
            ? null
            : ctx.Categories.FirstOrDefault(c => c.IdCategorie == affectation.IdCateg);
        var tolerance = categorie == null
            ? null
            : ctx.Tolerances.FirstOrDefault(t => t.IdCategorie == categorie.IdCategorie && !t.TypesTol);

        var week = new OvertimeWeekConfig(
            MultiPointage: ctx.MultiPointage,
            HeuresSemaine: categorie?.HeureSemaine ?? OvertimeRules.DefaultWeeklyHours,
            PauseCategorie: categorie?.Pause,
            ToleranceEntreeMinutes: tolerance?.Tolerance ?? 0,
            PasSortieMinutes: tolerance?.Sortie,
            CotationNuit: Cotation(ctx.Majorations, "NUIT"),
            CotationDimanche: Cotation(ctx.Majorations, "DIMANCHE"),
            CotationFerie: Cotation(ctx.Majorations, "FERIE"),
            EstFemme: OvertimeDayRules.EstFemme(ctx.Civilites.GetValueOrDefault(MlDatasetProjection.MlKey(card.SageMatricule))));

        return new EmployeeConfig(week, categorie?.IdCategorie, categorie?.HeureSemaine);
    }

    internal static decimal Cotation(IReadOnlyList<Majoration> majorations, string type)
        => majorations
            .Where(m => string.Equals(m.Mojoration?.Trim(), type, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Cotation ?? 0m)
            .DefaultIfEmpty(0m)
            .Max();

    // ------------------------------------------------------------------ jours

    internal static void BuildDays(DatasetContext ctx)
    {
        foreach (var card in ctx.Cards)
        {
            var cle = MlDatasetProjection.MlKey(card.SageMatricule);
            var days = new MlEmployeeCalendar(ctx, card, cle).Days();
            ctx.EmployeeDays[cle] = days;
            ComputeWeekResults(ctx, cle, ctx.Configs[cle], days);
        }
    }

    /// <summary>
    /// Résultats hebdomadaires, base des features HS et des labels : la semaine validée prime sur
    /// le recalcul.
    /// </summary>
    private static void ComputeWeekResults(DatasetContext ctx, string cle, EmployeeConfig config, List<DayRow> days)
    {
        foreach (var groupe in days.GroupBy(d => MlDatasetProjection.MondayOf(d.Date)))
        {
            var result = OvertimeCalculator.CalculerSemaine(
                config.Week,
                groupe.OrderBy(d => d.Date).Select(d => d.OvertimeDay));

            ctx.WeekResults[(cle, groupe.Key)] = ctx.HsValidees.TryGetValue((cle, groupe.Key), out var validee)
                ? new WeekComputation(
                    validee.Hs ?? 0m,
                    validee.Nuit ?? 0m,
                    validee.Dimanche ?? 0m,
                    validee.Feries ?? 0m,
                    validee.Retard ?? 0m,
                    validee.Absence ?? 0m,
                    Validee: true)
                : new WeekComputation(
                    result.Hs, result.Nuit, result.Dimanche, result.Ferie,
                    result.Retard, result.Absence, Validee: false);
        }
    }

    internal static PlanningSalarie? SelectPlan(IEnumerable<PlanningSalarie> plans)
        => plans
            .OrderBy(p => p.OffP == true ? 1 : 0)
            .ThenBy(p => p.Id)
            .FirstOrDefault();

    internal static decimal HeuresTravaillees(IReadOnlyList<PunchPair> paires)
    {
        decimal total = 0m;
        foreach (var paire in paires)
        {
            var debut = paire.EntreeDate.Date + paire.EntreeHeure;
            var fin = paire.SortieDate.Date + paire.SortieHeure;
            if (fin < debut)
                fin = fin.AddDays(1);
            total += (decimal)(fin - debut).TotalHours;
        }
        return Math.Round(total, 2);
    }

    // ------------------------------------------------------------------ features

    internal static void ComputeRollingFeatures(DatasetContext ctx)
    {
        foreach (var (cle, days) in ctx.EmployeeDays)
        {
            var history = new List<DayRow>();
            foreach (var day in days.OrderBy(d => d.Date))
            {
                ApplyRollingWindows(day, history);
                var lundi = MlDatasetProjection.DerniereSemaineConnue(ctx, day.Date);
                day.HeuresSupp4Semaines = Math.Round(
                    Enumerable.Range(1, 4).Sum(i => ctx.WeekResults.GetValueOrDefault((cle, lundi.AddDays(-7 * i)))?.Hs ?? 0m),
                    2);

                // Seuls les jours réellement observés alimentent l'historique : les jours futurs
                // (sans pointage par construction) et les trous de données n'y entrent pas.
                if (day.Date < ctx.ObservedUntil && !ctx.JoursSansPointage.Contains(day.Date))
                    history.Add(day);
            }
        }
    }

    /// <summary>Absences, retards et jours travaillés observés sur les 7, 30 et 90 jours précédents.</summary>
    private static void ApplyRollingWindows(DayRow day, List<DayRow> history)
    {
        var fenetre7 = history.Where(h => h.Date > day.Date.AddDays(-7)).ToList();
        var fenetre30 = history.Where(h => h.Date > day.Date.AddDays(-30)).ToList();
        day.Absences7j = fenetre7.Count(h => h.AbsenceNonJustifiee);
        day.Absences30j = fenetre30.Count(h => h.AbsenceNonJustifiee);
        day.Absences90j = history.Count(h => h.Date > day.Date.AddDays(-90) && h.AbsenceNonJustifiee);
        var planifies30 = fenetre30.Count(h => h.EstJourPlanifie);
        day.TauxAbsence30j = planifies30 == 0 ? 0d : (double)day.Absences30j / planifies30;
        day.Retards30j = fenetre30.Count(h => h.RetardMinutes > 0);
        day.JoursTravailles7j = fenetre7.Count(h => h.HeuresTravaillees > 0m);

        var derniereAbsence = history.Where(h => h.AbsenceNonJustifiee).OrderByDescending(h => h.Date).FirstOrDefault();
        day.JoursDepuisDerniereAbsence = derniereAbsence == null ? null : (day.Date - derniereAbsence.Date).Days;
    }

    internal static void ComputeTeamFeatures(DatasetContext ctx)
    {
        var all = ctx.EmployeeDays.Values.SelectMany(d => d).ToList();
        foreach (var dept in all.GroupBy(d => d.Departement ?? string.Empty))
        {
            var parDate = dept.GroupBy(d => d.Date).ToDictionary(g => g.Key, g => g.ToList());
            bool Observe(DateTime date) => date < ctx.ObservedUntil && !ctx.JoursSansPointage.Contains(date);
            var comptes = parDate.ToDictionary(
                kvp => kvp.Key,
                kvp => Observe(kvp.Key)
                    ? (Planned: kvp.Value.Count(d => d.EstJourPlanifie), Absents: kvp.Value.Count(d => d.AbsenceNonJustifiee))
                    : (Planned: 0, Absents: 0));
            ApplyTeamAbsenceRate(parDate, comptes, ctx.ObservedUntil);
        }

        var parJour = all
            .GroupBy(d => d.Date)
            .ToDictionary(
                g => g.Key,
                g => (Planned: g.Count(d => d.EstJourPlanifie), Conges: g.Count(d => d.Conge)));
        foreach (var day in all)
        {
            if (parJour.TryGetValue(day.Date, out var stats))
            {
                day.EffectifPrevuJour = stats.Planned;
                day.CongesPrevusJour = stats.Conges;
            }
        }
    }

    /// <summary>
    /// Taux d'absence de l'équipe sur la fenêtre glissante des 30 jours précédents ; pour les jours
    /// futurs, le taux reste celui des 30 derniers jours observés.
    /// </summary>
    private static void ApplyTeamAbsenceRate(
        Dictionary<DateTime, List<DayRow>> parDate,
        Dictionary<DateTime, (int Planned, int Absents)> comptes,
        DateTime observedUntil)
    {
        var fenetre = new Queue<DateTime>();
        var (plannedSum, absentsSum) = (0, 0);
        double? tauxFige = null;
        foreach (var date in parDate.Keys.OrderBy(d => d))
        {
            while (fenetre.Count > 0 && fenetre.Peek() < date.AddDays(-30))
            {
                var sortant = comptes[fenetre.Dequeue()];
                plannedSum -= sortant.Planned;
                absentsSum -= sortant.Absents;
            }

            var taux = plannedSum == 0 ? 0d : (double)absentsSum / plannedSum;
            if (date >= observedUntil)
                taux = tauxFige ??= taux;
            foreach (var row in parDate[date])
                row.TauxAbsenceEquipe30j = taux;

            fenetre.Enqueue(date);
            plannedSum += comptes[date].Planned;
            absentsSum += comptes[date].Absents;
        }
    }

}
