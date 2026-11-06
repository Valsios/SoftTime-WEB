using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

public static class HolidayPunchRules
{

    /// <summary>
    /// 3.7 — Jour férié où le salarié est planifié avec heures (ni OFF ni Pause) et où aucune
    /// correction du jour ne porte le férié (<c>FERIER</c>, <c>FERIER_AM</c>, <c>FERIER_PM</c> ou
    /// <c>M_FERIES</c> &gt; 0). Planning retenu comme dans <see cref="AbsenceReportRules"/> : une
    /// ligne de travail prime sur une ligne OFF, puis le plus petit ID. Aucun jour futur.
    /// </summary>
    public static IReadOnlyList<ClosingAnomaly> FeriesNonAppliques(
        IReadOnlyList<CardPaie> cartes,
        IReadOnlyList<PlanningSalarie> plannings,
        IReadOnlyList<HeureCorrigee> corrections,
        IReadOnlySet<DateTime> feries,
        DateTime from,
        DateTime to,
        DateTime dateDuJour,
        ClosingPunchIndex? pointages = null)
    {
        var debut = from.Date;
        var hier = dateDuJour.Date.AddDays(-1);
        var fin = to.Date < hier ? to.Date : hier;
        if (debut > fin)
            return Array.Empty<ClosingAnomaly>();

        var cleParIdSal = ClosingAnomalyFactory.CleParIdSal(cartes);

        var planningRetenu = new Dictionary<(string Cle, DateTime Jour), PlanningSalarie>();
        foreach (var plan in plannings)
        {
            if (plan.DateP is not DateTime dateP || !cleParIdSal.TryGetValue(plan.IdSal, out var cle))
                continue;
            var jour = dateP.Date;
            if (jour < debut || jour > fin || !feries.Contains(jour))
                continue;

            var cleJour = (cle, jour);
            if (!planningRetenu.TryGetValue(cleJour, out var existant) || ClosingAnomalyFactory.PrimeSur(plan, existant))
                planningRetenu[cleJour] = plan;
        }

        var joursFeriesAppliques = corrections
            .Where(h => h.DatePointageIn.HasValue && !string.IsNullOrWhiteSpace(h.MatriculeSage))
            .Where(h => h.Ferier == true || h.FerierAm == true || h.FerierPm == true || h.MFeries > 0)
            .Select(h => (Cle: MatriculeIdentity.Cle(h.MatriculeSage), Jour: h.DatePointageIn!.Value.Date))
            .ToHashSet();

        var resultats = new List<ClosingAnomaly>();
        foreach (var (cleJour, plan) in planningRetenu)
        {
            if (AbsenceReportRules.EstJourOff(plan))
                continue;
            var duree = AbsenceReportRules.CalculerDureeHeures(plan);
            if (duree is null || joursFeriesAppliques.Contains(cleJour))
                continue;

            resultats.Add(ClosingAnomalyFactory.Anomalie(
                ClosingAnomalyType.FerieNonApplique, cleJour.Cle, cleJour.Jour,
                "drapeau férié absent (sans effet sur le calcul de la paie)", duree,
                pointageExiste: pointages?.APointage(cleJour.Cle, cleJour.Jour)));
        }

        return PayrollClosingRules.Trier(resultats);
    }
}
