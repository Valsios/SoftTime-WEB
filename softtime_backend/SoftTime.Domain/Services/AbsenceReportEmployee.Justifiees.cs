using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

/// <summary>
/// Absences <b>justifiées</b> d'un salarié (état informatif, sans effet sur la paie) : jours planifiés travaillés,
/// en semaine, ni fériés ni de repos, couverts par un événement SAGE ou par un arbitrage dont le code n'est pas
/// compté comme non justifié.
/// </summary>
internal sealed partial class AbsenceReportEmployee
{
    /// <summary>
    /// Lignes justifiées de la période. Jours candidats : jours planifiés avec heures (le repos et les jours sans heures
    /// n'en font pas partie), sans week-end ni férié. Un événement SAGE l'emporte sur une ligne ou un arbitrage du même
    /// jour ; un événement dont le libellé correspond à un code compté (par défaut « ABSENCE NON JUSTIFIEE ») n'est pas
    /// une justification et est ignoré, comme un arbitrage dont le code est compté.
    /// </summary>
    public IEnumerable<AbsenceReportRow> LignesJustifiees(
        ReportCalendar calendrier, IEnumerable<DateTime> datesPlanifiees, AbsenceCodesComptes codes)
    {
        var evenements = _conges
            .Where(c => !codes.CorrespondA(c.Libelle))
            .GroupBy(c => c.Date.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var jour in JoursOuvres(datesPlanifiees.Where(calendrier.Contient), calendrier))
        {
            var plan = calendrier.Planning(_cle, jour);

            var ligne = evenements.TryGetValue(jour, out var duJour)
                ? LigneEvenementSage(jour, plan, duJour)
                : LigneArbitrageJustifie(jour, plan, codes);
            if (ligne is not null)
                yield return ligne;
        }
    }

    /// <summary>Jour couvert par un ou plusieurs événements SAGE retenus : valeurs cumulées (plafonnées à 1), motifs distincts.</summary>
    private AbsenceReportRow LigneEvenementSage(DateTime jour, PlanningSalarie? plan, List<LeaveDay> evenements)
    {
        var valeur = Math.Min(1m, evenements.Sum(c => c.Valeur));
        var periode = valeur >= 1m ? PeriodeJournee : PeriodeDemiJournee(evenements);
        var motif = CumulerMotifs(evenements.Select(c => !string.IsNullOrWhiteSpace(c.Libelle) ? c.Libelle : c.Code));
        return LigneJustifiee(jour, plan, AbsenceReportRules.TypeEvenementSage, periode, valeur, motif);
    }

    /// <summary>
    /// Jour sans événement SAGE couvert par un arbitrage justifié. Une seule ligne arbitrée au code compté suffit à compter
    /// la journée comme non justifiée (même règle que la variable Absence) : elle n'est alors pas listée ici.
    /// </summary>
    private AbsenceReportRow? LigneArbitrageJustifie(DateTime jour, PlanningSalarie? plan, AbsenceCodesComptes codes)
    {
        var arbitrees = _correctionsParJour.GetValueOrDefault(jour)?.Where(EstArbitree).ToList();
        if (arbitrees is null || arbitrees.Count == 0 || arbitrees.Any(l => codes.Compte(l.IntituleAbsence)))
            return null;

        var matin = arbitrees.Any(l => l.AbsenceAm == true);
        var apresMidi = arbitrees.Any(l => l.AbsencePm == true);
        var periode = (matin, apresMidi) switch
        {
            (true, false) => PeriodeMatin,
            (false, true) => PeriodeApresMidi,
            _ => PeriodeJournee
        };
        var motif = CumulerMotifs(arbitrees.Select(l => l.IntituleAbsence));
        return LigneJustifiee(jour, plan, AbsenceReportRules.TypeArbitrage, periode, periode == PeriodeJournee ? 1m : 0.5m, motif);
    }

    private AbsenceReportRow LigneJustifiee(
        DateTime jour, PlanningSalarie? plan, string type, string periode, decimal valeur, string motif)
    {
        var duree = AbsenceReportRules.CalculerDureeHeures(plan);
        if (duree.HasValue && valeur < 1m)
            duree = Math.Round(duree.Value * valeur, 2);
        return Ligne(jour, type) with { Periode = periode, DureeHeures = duree, Motif = motif };
    }

    /// <summary>Moitié couverte quand tous les événements du jour désignent la même ; « Demi-journée » si elle est inconnue.</summary>
    private static string PeriodeDemiJournee(List<LeaveDay> evenements)
    {
        var moities = evenements.Select(c => c.Moitie).Distinct().ToList();
        return moities.Count == 1 && moities[0] is { } moitie
            ? (moitie == LeaveHalf.Matin ? PeriodeMatin : PeriodeApresMidi)
            : PeriodeDemi;
    }

    /// <summary>Motifs distincts (casse ignorée), triés, séparés par « + » ; « Motif non renseigné » si aucun.</summary>
    private static string CumulerMotifs(IEnumerable<string?> motifs)
    {
        var distincts = motifs
            .Select(m => m?.Trim())
            .Where(m => !string.IsNullOrEmpty(m))
            .Select(m => m!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return distincts.Count == 0 ? AbsenceReportRules.MotifNonRenseigne : string.Join(" + ", distincts);
    }

    private const string PeriodeJournee = "Journée";
    private const string PeriodeDemi = "Demi-journée";
    private const string PeriodeMatin = "Matin";
    private const string PeriodeApresMidi = "Après-midi";
}
