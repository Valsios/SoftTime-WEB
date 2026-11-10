using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Dashboard;

/// <summary>
/// Relève absences, présences, retards et jours planifiés de chaque carte, avec les mêmes règles
/// que les états (rapports).
/// </summary>
internal sealed class DashboardEventCollector
{
    /// <summary>Planning vide partagé : évite une allocation par salarié sans planning.</summary>
    private static readonly Dictionary<DateTime, PlanningSalarie> PlansVides = new();

    private readonly DashboardData _data;
    private readonly List<(string Cle, DateTime Date)> _absences = new();
    private readonly List<(string Cle, DateTime Date)> _presences = new();
    private readonly List<(string Cle, DateTime Date, int Minutes)> _retards = new();
    private int _joursPlanifies;

    public DashboardEventCollector(DashboardData data) => _data = data;

    public void Collecter(CardPaie carte)
    {
        var cle = DashboardIndicators.Cle(carte.SageMatricule);
        var lignes = _data.CorrectionsParCle.GetValueOrDefault(cle) ?? new List<HeureCorrigee>();
        var plans = _data.Planning.GetValueOrDefault(carte.Id) ?? PlansVides;

        CollecterAbsencesExplicites(cle, lignes, plans);
        CollecterJoursPlanifies(cle, lignes, plans);
        CollecterPresences(cle, lignes);
        CollecterRetards(carte, lignes, plans);
    }

    /// <summary>Une journée = une ligne : plusieurs heures corrigées le même jour ne comptent qu'une fois.</summary>
    public DashboardEvents Resultat() => new(
        _absences.Distinct().ToList(),
        _presences.Distinct().ToList(),
        _retards
            .GroupBy(r => (r.Cle, r.Date))
            .Select(g => (g.Key.Cle, g.Key.Date, Minutes: g.Sum(r => r.Minutes)))
            .ToList(),
        _joursPlanifies);

    /// <summary>
    /// Absences explicites : l'absence est portée par l'heure corrigée. Un jour de repos planifié
    /// (OFF) n'est jamais une absence, même si une correction le mentionne.
    /// </summary>
    private void CollecterAbsencesExplicites(
        string cle, List<HeureCorrigee> lignes, Dictionary<DateTime, PlanningSalarie> plans)
    {
        foreach (var ligne in lignes.Where(h => h.AbsenceAm == true || h.AbsencePm == true))
        {
            if (ligne.DatePointageIn is not DateTime jour)
                continue;
            var jourDate = jour.Date;
            if (_data.EstExclu(jourDate) || AbsenceReportRules.EstJourOff(plans.GetValueOrDefault(jourDate)))
                continue;
            _absences.Add((cle, jourDate));
        }
    }

    private void CollecterJoursPlanifies(
        string cle, List<HeureCorrigee> lignes, Dictionary<DateTime, PlanningSalarie> plans)
    {
        var conges = _data.CongesParCle.TryGetValue(cle, out var joursConge) ? joursConge : new List<LeaveDay>();
        foreach (var (jour, plan) in plans)
        {
            if (jour > _data.FinEvenements)
                continue;

            var estNonTravaille = DashboardIndicators.EstNonTravaille(jour, _data.Feries);
            var estConge = LeaveRules.EstConge(conges, jour);
            // LOT 9 : un jour de conge SAGE n'est ni une presence theorique ni une absence, comme
            // dans la validation semaine — il sort donc aussi du denominateur de l'absenteisme.
            // Jour de repos planifié (OFF) ou shift sans heures : aucune journée attendue.
            if (!DashboardRules.CompteDansPresenceTheorique(estNonTravaille, estConge)
                || AbsenceReportRules.CalculerDureeHeures(plan) is null)
                continue;

            _joursPlanifies++;
            // Règle partagée avec l'état des absences : jour planifié avec heures, sans pointage.
            var aPointage = lignes.Any(h => h.DatePointageIn?.Date == jour);
            if (AbsenceReportRules.EstNonJustifiee(estNonTravaille, estConge, plan, aPointage))
                _absences.Add((cle, jour));
        }
    }

    /// <summary>
    /// Présence : jour réellement pointé (entrée saisie) hors week-end et férié. C'est le
    /// dénominateur du taux de ponctualité.
    /// </summary>
    private void CollecterPresences(string cle, List<HeureCorrigee> lignes)
    {
        foreach (var ligne in lignes.Where(h => h.DatePointageIn.HasValue && h.HEntree.HasValue))
        {
            var jour = ligne.DatePointageIn!.Value.Date;
            if (!_data.EstExclu(jour))
                _presences.Add((cle, jour));
        }
    }

    /// <summary>
    /// Retards : règle partagée avec l'état des retards (valeur saisie prioritaire, sinon première
    /// entrée du jour vs planning + tolérance d'entrée). On raisonne par journée pour ne pas compter
    /// l'entrée d'une deuxième paire (après-midi) comme un retard.
    /// </summary>
    private void CollecterRetards(
        CardPaie carte, List<HeureCorrigee> lignes, Dictionary<DateTime, PlanningSalarie> plans)
    {
        var cle = DashboardIndicators.Cle(carte.SageMatricule);
        var tolerance = RetardReportRules.ToleranceEntree(
            carte.Id, _data.Perimetre.CategorieParCarte, _data.Tolerances);
        foreach (var groupeJour in lignes
                     .Where(h => h.DatePointageIn.HasValue)
                     .GroupBy(h => h.DatePointageIn!.Value.Date))
        {
            var jour = groupeJour.Key;
            if (_data.EstExclu(jour))
                continue;

            var minutes = MinutesRetard(groupeJour, plans.GetValueOrDefault(jour), tolerance);
            if (minutes > 0)
                _retards.Add((cle, jour, minutes));
        }
    }

    private static int MinutesRetard(IEnumerable<HeureCorrigee> lignesDuJour, PlanningSalarie? plan, int tolerance) =>
        RetardReportRules.MinutesRetardJour(lignesDuJour.ToList(), plan, tolerance);
}
