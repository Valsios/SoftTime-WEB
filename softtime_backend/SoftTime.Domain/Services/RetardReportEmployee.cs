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
        var premiereEntree = RetardReportRules.PremiereEntree(lignes);
        var minutes = RetardReportRules.MinutesRetardJour(lignes, plan, Tolerance);
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
            RetardSaisi: lignes.Any(h => h.Retard.HasValue));
    }
}
