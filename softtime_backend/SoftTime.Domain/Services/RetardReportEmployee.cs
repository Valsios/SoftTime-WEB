using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Domain.Services;

/// <summary>Retards d'un salarié : une ligne par jour ouvré de la période dont le retard est positif.</summary>
internal sealed record RetardReportEmployee(string Cle, CardPaie Carte, List<HeureCorrigee> Corrections, int Tolerance)
{
    public IEnumerable<RetardReportRow> Retards(ReportCalendar calendrier) =>
        Corrections
            .Where(h => h.DatePointageIn.HasValue)
            .GroupBy(h => h.DatePointageIn!.Value.Date)
            .Where(g => calendrier.Contient(g.Key) && !AbsenceReportRules.EstNonTravaille(g.Key, calendrier.Feries))
            .Select(g => RetardDuJour(g.Key, g.ToList(), calendrier.Planning(Cle, g.Key)))
            .OfType<RetardReportRow>();

    private RetardReportRow? RetardDuJour(DateTime jour, List<HeureCorrigee> lignes, PlanningSalarie? plan)
    {
        var heurePrevue = RetardReportRules.HeurePrevue(plan);
        var entrees = lignes
            .Where(h => h.HEntree.HasValue)
            .Select(h => h.HEntree!.Value)
            .ToList();
        TimeSpan? premiereEntree = entrees.Count > 0 ? entrees.Min() : null;
        var retardsSaisis = lignes.Select(h => h.Retard).ToList();

        var minutes = RetardReportRules.MinutesRetardJour(
            absenceMatin: lignes.Any(h => h.AbsenceAm == true),
            retardsSaisis: retardsSaisis,
            premiereEntree: premiereEntree,
            heurePrevue: heurePrevue,
            toleranceMinutes: Tolerance);
        if (minutes <= 0)
            return null;

        return new RetardReportRow(
            Matricule: !string.IsNullOrWhiteSpace(Carte.SageMatricule) ? Carte.SageMatricule.Trim() : Cle,
            Nom: Carte.SageNom?.Trim(),
            Prenom: Carte.SagePrenom?.Trim(),
            Departement: Carte.Departement?.Trim(),
            Date: jour,
            HeureEntree: premiereEntree,
            HeurePrevue: heurePrevue,
            ToleranceMinutes: Tolerance,
            MinutesRetard: minutes,
            RetardSaisi: retardsSaisis.Any(r => r.HasValue));
    }
}
