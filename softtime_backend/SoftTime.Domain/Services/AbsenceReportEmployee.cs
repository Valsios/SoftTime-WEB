using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

/// <summary>
/// Absences d'un salarié (toutes ses cartes partagent la même clé matricule) : congés SAGE,
/// absences arbitrées dans la GTA et jours planifiés sans pointage.
/// </summary>
internal sealed class AbsenceReportEmployee
{
    private readonly string _cle;
    private readonly CardPaie _carte;
    private readonly List<HeureCorrigee> _corrections;
    private readonly List<LeaveDay> _conges;
    private readonly Dictionary<DateTime, List<HeureCorrigee>> _correctionsParJour;
    private readonly Dictionary<DateTime, (decimal Valeur, string? Motif)> _congesParJour;

    public AbsenceReportEmployee(string cle, CardPaie carte, List<HeureCorrigee> corrections, List<LeaveDay> conges)
    {
        _cle = cle;
        _carte = carte;
        _corrections = corrections;
        _conges = conges;
        _correctionsParJour = corrections
            .Where(h => h.DatePointageIn.HasValue)
            .GroupBy(h => h.DatePointageIn!.Value.Date)
            .ToDictionary(g => g.Key, g => g.ToList());
        _congesParJour = conges
            .GroupBy(c => c.Date.Date)
            .ToDictionary(g => g.Key, g => CumulerConges(g.ToList()));
    }

    public IEnumerable<AbsenceReportRow> Lignes(AbsenceReportCalendar calendrier, IEnumerable<DateTime> datesPlanifiees)
    {
        foreach (var jour in DatesCandidates(calendrier, datesPlanifiees).OrderBy(d => d))
        {
            // C3 : pour tous les types, week-ends et fériés sont exclus.
            if (AbsenceReportRules.EstNonTravaille(jour, calendrier.Feries))
                continue;

            var ligne = LigneDuJour(jour, calendrier.Planning(_cle, jour));
            if (ligne is not null)
                yield return ligne;
        }
    }

    /// <summary>C12 : plusieurs congés le même jour cumulent leurs valeurs (plafonnées à 1) et leurs codes.</summary>
    private static (decimal Valeur, string? Motif) CumulerConges(List<LeaveDay> conges)
    {
        var valeurTotale = Math.Min(1m, conges.Sum(c => c.Valeur));
        var codes = conges.Select(c => c.Code?.Trim())
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var motif = string.Join(", ", codes);
        return (valeurTotale, string.IsNullOrEmpty(motif) ? null : motif);
    }

    /// <summary>Congés SAGE, absences arbitrées et jours planifiés travaillés de la période.</summary>
    private HashSet<DateTime> DatesCandidates(AbsenceReportCalendar calendrier, IEnumerable<DateTime> datesPlanifiees)
    {
        var dates = new HashSet<DateTime>();
        dates.UnionWith(_conges.Select(c => c.Date.Date).Where(calendrier.Contient));
        dates.UnionWith(_corrections
            .Where(h => h.DatePointageIn.HasValue && EstArbitree(h))
            .Select(h => h.DatePointageIn!.Value.Date)
            .Where(calendrier.Contient));
        dates.UnionWith(datesPlanifiees);
        return dates;
    }

    private AbsenceReportRow? LigneDuJour(DateTime jour, PlanningSalarie? plan)
    {
        // Priorité 0 : jour de repos planifié (planning OFF) -> aucune ligne, quel que soit
        // le type. Un repos n'est ni une absence ni une journée attendue.
        if (AbsenceReportRules.EstJourOff(plan))
            return null;
        // Priorité 1 : congé SAGE.
        if (_congesParJour.TryGetValue(jour, out var conge))
            return LigneConge(jour, plan, conge);
        // Priorité 2 : arbitrée GTA.
        var arbitree = _correctionsParJour.GetValueOrDefault(jour)?.FirstOrDefault(EstArbitree);
        if (arbitree is not null)
            return LigneArbitree(jour, plan, arbitree);
        // Priorité 3 : non justifiée (jour planifié avec heures, sans aucun pointage).
        var aPointage = _correctionsParJour.ContainsKey(jour);
        return AbsenceReportRules.EstNonJustifiee(estNonTravaille: false, estConge: false, plan, aPointage)
            ? Ligne(jour, AbsenceReportRules.TypeNonJustifiee) with { DureeHeures = AbsenceReportRules.CalculerDureeHeures(plan) }
            : null;
    }

    private AbsenceReportRow LigneConge(DateTime jour, PlanningSalarie? plan, (decimal Valeur, string? Motif) conge)
    {
        var periode = conge.Valeur >= 1m ? "Journée" : "Demi-journée";
        var duree = AbsenceReportRules.CalculerDureeHeures(plan);
        if (duree.HasValue)
            duree = Math.Round(duree.Value * conge.Valeur, 2);
        return Ligne(jour, AbsenceReportRules.TypeCongeSage) with { Periode = periode, DureeHeures = duree, Motif = conge.Motif };
    }

    /// <summary>C4 : une absence arbitrée sur une seule demi-journée vaut la moitié de la durée planifiée.</summary>
    private AbsenceReportRow LigneArbitree(DateTime jour, PlanningSalarie? plan, HeureCorrigee arbitree)
    {
        var periode = (arbitree.AbsenceAm == true, arbitree.AbsencePm == true) switch
        {
            (true, false) => "Matin",
            (false, true) => "Après-midi",
            _ => "Journée"
        };
        var duree = AbsenceReportRules.CalculerDureeHeures(plan);
        if (duree.HasValue && periode != "Journée")
            duree = Math.Round(duree.Value * 0.5m, 2);
        return Ligne(jour, AbsenceReportRules.TypeArbitreeGta) with { Periode = periode, DureeHeures = duree, Motif = arbitree.IntituleAbsence };
    }

    /// <summary>Ligne d'une journée entière, sans durée ni motif, à compléter selon le type.</summary>
    private AbsenceReportRow Ligne(DateTime jour, string type)
    {
        var matricule = !string.IsNullOrWhiteSpace(_carte.SageMatricule) ? _carte.SageMatricule.Trim() : _cle;
        return new AbsenceReportRow(
            Matricule: matricule,
            Nom: _carte.SageNom?.Trim(),
            Prenom: _carte.SagePrenom?.Trim(),
            Departement: _carte.Departement?.Trim(),
            Date: jour,
            Periode: "Journée",
            DureeHeures: null,
            Type: type,
            Motif: null);
    }

    private static bool EstArbitree(HeureCorrigee h) => h.AbsenceAm == true || h.AbsencePm == true;
}
