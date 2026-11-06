using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.PunchProcessing;

/// <summary>
/// Une exécution du pré-traitement : parcourt chaque salarié et chaque jour de la période et
/// accumule corrections, anomalies créées, anomalies mises à jour et anomalies à supprimer.
/// </summary>
internal sealed class PunchProcessingRun
{
    private static readonly IReadOnlyDictionary<DateTime, PunchPlanningWindow> SansPlanning =
        new Dictionary<DateTime, PunchPlanningWindow>();

    private readonly PunchProcessingData _data;
    private readonly PunchProtection _protection;
    private readonly IRepository<HeureAnomalie> _anomalies;
    private readonly Dictionary<(string Cle, DateTime Jour), List<HeureAnomalie>> _anomaliesParJour;
    private readonly HashSet<decimal> _anomaliesASupprimer = new();
    private readonly HashSet<(string Cle, DateTime Jour)> _anomaliesCreeesJour = new();

    public PunchProcessingRun(PunchProcessingData data, PunchProtection protection, IRepository<HeureAnomalie> anomalies)
    {
        _data = data;
        _protection = protection;
        _anomalies = anomalies;
        _anomaliesParJour = data.AnomaliesExistantes
            .Where(a => a.DatePointageIn.HasValue && !string.IsNullOrWhiteSpace(a.MatriculeSage))
            .GroupBy(a => (Cle: MatriculeIdentity.Cle(a.MatriculeSage), Jour: a.DatePointageIn!.Value.Date))
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Id).ToList());
    }

    public List<HeureCorrigee> NouvellesCorrections { get; } = new();

    public List<HeureAnomalie> NouvellesAnomalies { get; } = new();

    public int AnomaliesMisesAJour { get; private set; }

    public int JoursIgnores { get; private set; }

    /// <summary>Anomalies existantes devenues sans objet ou doublons.</summary>
    public List<HeureAnomalie> AnomaliesASupprimer() =>
        _data.AnomaliesExistantes.Where(a => _anomaliesASupprimer.Contains(a.Id)).ToList();

    public void TraiterCarte(CardPaie card)
    {
        var matricule = (card.SageMatricule ?? string.Empty).Trim();
        if (matricule.Length == 0)
            return;

        var cle = MatriculeIdentity.Cle(matricule);
        var planningCarte = _data.PlanningParCarte.GetValueOrDefault(card.Id)
                            ?? new Dictionary<DateTime, PlanningSalarie>();
        var momentsParJour = GrouperMoments(cle, planningCarte);

        for (var jour = _data.From; jour <= _data.To; jour = jour.AddDays(1))
        {
            if (_protection.EstProtege(cle, jour))
            {
                JoursIgnores++;
                continue;
            }

            var moments = momentsParJour.TryGetValue(jour, out var liste) ? liste : Array.Empty<PunchMoment>();
            TraiterJour(new PunchDay(matricule, cle, jour), moments, planningCarte.GetValueOrDefault(jour));
        }
    }

    private IReadOnlyDictionary<DateTime, IReadOnlyList<PunchMoment>> GrouperMoments(
        string cle, Dictionary<DateTime, PlanningSalarie> planningCarte)
    {
        var punchs = _data.PointagesParCle.GetValueOrDefault(cle) ?? new List<PunchMoment>();
        if (!_data.MultiPointage)
        {
            // Le mode mono ne consulte jamais le planning pour l'appariement : chaque journée
            // calendaire est traitée séparément (même si un poste de nuit existe au planning).
            return PunchDayGrouping.Build(punchs, SansPlanning, _data.From, _data.To);
        }

        var fenetres = planningCarte
            .Where(kv => kv.Value.OffP != true)
            .ToDictionary(
                kv => kv.Key,
                kv => new PunchPlanningWindow(kv.Key, kv.Value.Ha, kv.Value.Hd, kv.Value.OffP == true));
        return PunchDayGrouping.Build(punchs, fenetres, _data.From, _data.To);
    }

    private void TraiterJour(PunchDay day, IReadOnlyList<PunchMoment> moments, PlanningSalarie? plan)
    {
        var estFerie = _data.Feries.Contains(day.Jour);
        if (moments.Count == 0)
        {
            if (EstTravaille(day.Jour, plan, estFerie))
                AjouterAnomalie(day, PunchAnomalyValues.Absence);
            return;
        }

        if (_data.MultiPointage)
            TraiterJourMulti(day, moments, estFerie);
        else
            TraiterJourMono(day, moments, estFerie);
    }

    private void TraiterJourMono(PunchDay day, IReadOnlyList<PunchMoment> moments, bool estFerie)
    {
        var resultat = PunchPairingRules.PairSimpleDay(moments);
        if (resultat.Unpaired.Count > 0)
        {
            var premier = resultat.Unpaired[0];
            AjouterAnomalie(day, PunchAnomalyValues.Heures(premier.Heure, premier.Heure, estFerie));
            return;
        }

        EnregistrerPaires(day, resultat.Pairs);
    }

    private void TraiterJourMulti(PunchDay day, IReadOnlyList<PunchMoment> moments, bool estFerie)
    {
        var resultat = PunchPairingRules.PairMultipleDay(moments);
        if (resultat.Unpaired.Count > 0)
        {
            var heures = PunchAnomalyValues.Heures(resultat.Unpaired[0].Heure, resultat.Unpaired[^1].Heure, estFerie);
            AjouterAnomalie(day, heures);
            return;
        }

        if (resultat.Pairs.Count == 0)
        {
            // Cas défensif : moments non vides mais aucune paire ni orphelin.
            AjouterAnomalie(day, PunchAnomalyValues.Heures(moments[0].Heure, moments[^1].Heure, estFerie));
            return;
        }

        EnregistrerPaires(day, resultat.Pairs);
    }

    private void EnregistrerPaires(PunchDay day, IEnumerable<PunchPair> paires)
    {
        foreach (var paire in paires)
            AjouterCorrection(day.Matricule, paire);
        SupprimerAnomaliesDuJour(day);
    }

    private bool EstTravaille(DateTime jour, PlanningSalarie? plan, bool estFerie)
    {
        if (estFerie)
            return false;
        if (plan is not null)
            return plan.OffP != true;
        // En mono, le desktop signale les jours ouvrés vides même sans planning. En multi,
        // le planning est la référence : sans planning, aucune absence n'est attendue.
        return !_data.MultiPointage
               && jour.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
    }

    private void SupprimerAnomaliesDuJour(PunchDay day)
    {
        if (!_anomaliesParJour.TryGetValue((day.Cle, day.Jour), out var existantes))
            return;
        foreach (var anomalie in existantes)
            _anomaliesASupprimer.Add(anomalie.Id);
    }

    private void AjouterAnomalie(PunchDay day, PunchAnomalyValues valeurs)
    {
        if (!_anomaliesCreeesJour.Add((day.Cle, day.Jour)))
            return;
        if (MettreAJourAnomalieExistante(day, valeurs))
            return;

        var anomalie = new HeureAnomalie();
        Renseigner(anomalie, day, valeurs);
        NouvellesAnomalies.Add(anomalie);
    }

    /// <summary>Une seule anomalie par (matricule, jour) : l'existante est mise à jour, ses doublons supprimés.</summary>
    private bool MettreAJourAnomalieExistante(PunchDay day, PunchAnomalyValues valeurs)
    {
        if (!_anomaliesParJour.TryGetValue((day.Cle, day.Jour), out var existantes))
            return false;
        var aGarder = existantes.Where(a => !_anomaliesASupprimer.Contains(a.Id)).ToList();
        if (aGarder.Count == 0)
            return false;

        var cible = aGarder[0];
        Renseigner(cible, day, valeurs);
        _anomalies.Update(cible);
        foreach (var doublon in aGarder.Skip(1))
            _anomaliesASupprimer.Add(doublon.Id);
        AnomaliesMisesAJour++;
        return true;
    }

    private void Renseigner(HeureAnomalie anomalie, PunchDay day, PunchAnomalyValues valeurs)
    {
        anomalie.BddSage = _data.SageDb;
        anomalie.BddPointeuse = _data.PointeuseDb;
        anomalie.MatriculeSage = day.Matricule;
        anomalie.DatePointageIn = day.Jour;
        anomalie.DatePointageOut = day.Jour;
        anomalie.H1 = valeurs.H1;
        anomalie.H2 = valeurs.H2;
        anomalie.AbsAm = valeurs.AbsenceAm;
        anomalie.AbsPm = valeurs.AbsencePm;
        anomalie.Feries = valeurs.EstFerie ? true : null;
        anomalie.FeriesAm = valeurs.EstFerie ? true : null;
        anomalie.FeriesPm = valeurs.EstFerie ? true : null;
    }

    private void AjouterCorrection(string matricule, PunchPair paire)
    {
        var ferieEntree = _data.Feries.Contains(paire.EntreeDate.Date);
        var ferieSortie = _data.Feries.Contains(paire.SortieDate.Date);
        NouvellesCorrections.Add(new HeureCorrigee
        {
            BddSage = _data.SageDb,
            BddPointeuse = _data.PointeuseDb,
            MatriculeSage = matricule,
            DatePointageIn = paire.EntreeDate.Date,
            DatePointageOut = paire.SortieDate.Date,
            HEntree = paire.EntreeHeure,
            HSortie = paire.SortieHeure,
            Ferier = ferieEntree || ferieSortie ? true : null,
            FerierAm = ferieSortie ? true : null,
            FerierPm = ferieEntree ? true : null,
            ValiderCorrection = true,
            Origine = PunchProcessingConfiguration.OrigineAuto
        });
    }
}
