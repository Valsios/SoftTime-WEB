using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

/// <summary>
/// Règles pures de calcul du rapport des absences.
/// Détermine les absences constatées (Congé SAGE, Arbitrée GTA, Non justifiée)
/// en croisant les plannings, les pointages/corrections et les congés SAGE.
///
/// Mêmes invariants que le tableau de bord analytique : un jour de repos planifié (<c>OffP</c>,
/// shift sans heures), un week-end, un férié ou un congé ne sont jamais des absences, et aucun
/// jour futur n'est produit. Le tableau de bord réutilise d'ailleurs
/// <see cref="EstNonJustifiee"/> et <see cref="CalculerDureeHeures"/> pour rester aligné.
/// </summary>
public static class AbsenceReportRules
{
    public const string TypeCongeSage = "Congé SAGE";
    public const string TypeArbitreeGta = "Arbitrée GTA";
    public const string TypeNonJustifiee = "Non justifiée";

    public static IReadOnlyList<AbsenceReportRow> Calculer(
        IReadOnlyList<PlanningSalarie> plannings,
        IReadOnlyList<CardPaie> cartes,
        IReadOnlyList<HeureCorrigee> corrections,
        IReadOnlyDictionary<string, List<LeaveDay>> congesParCle,
        IReadOnlySet<DateTime> feries,
        DateTime from,
        DateTime to,
        DateTime dateDuJour)
    {
        var debut = from.Date;
        var hier = dateDuJour.Date.AddDays(-1);

        // Aucun jour futur : la période demandée est plafonnée à la veille.
        var fin = to.Date < hier ? to.Date : hier;
        if (debut > fin)
            return Array.Empty<AbsenceReportRow>();

        // C11 : Toutes les cartes de même clé matricule sont mappées par leur IDSAL
        var cleParIdSal = new Dictionary<int, string>();
        var cartesParCle = new Dictionary<string, CardPaie>(StringComparer.OrdinalIgnoreCase);

        foreach (var carte in cartes)
        {
            var cle = MatriculeIdentity.Cle(carte.SageMatricule);
            if (string.IsNullOrEmpty(cle))
                continue;

            cleParIdSal[carte.Id] = cle;
            if (!cartesParCle.TryGetValue(cle, out var existante) || carte.Id < existante.Id)
                cartesParCle[cle] = carte;
        }

        if (cartesParCle.Count == 0)
            return Array.Empty<AbsenceReportRow>();

        // C11 : plannings de TOUTES les cartes de même clé pris en compte. Une ligne de travail
        // prime sur une ligne OFF, puis le plus petit ID l'emporte (choix déterministe).
        var planningsParCleEtDate = new Dictionary<(string Cle, DateTime Date), PlanningSalarie>();
        foreach (var plan in plannings)
        {
            if (plan.DateP is not DateTime dateP)
                continue;
            if (!cleParIdSal.TryGetValue(plan.IdSal, out var cle))
                continue;

            var jour = dateP.Date;
            if (jour < debut || jour > fin)
                continue;

            var key = (cle, jour);
            if (!planningsParCleEtDate.TryGetValue(key, out var existant) || PrimeSur(plan, existant))
                planningsParCleEtDate[key] = plan;
        }

        // C10 (PERFORMANCE) : dates réellement planifiées (jour travaillé avec heures) précalculées
        // une seule fois avant la boucle. Un jour OFF ou sans heures n'est jamais une absence.
        var datesPlanifieesParCle = new Dictionary<string, List<DateTime>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in planningsParCleEtDate)
        {
            var (cle, jour) = kvp.Key;
            if (EstJourOff(kvp.Value) || CalculerDureeHeures(kvp.Value) is null)
                continue;

            if (!datesPlanifieesParCle.TryGetValue(cle, out var liste))
            {
                liste = new List<DateTime>();
                datesPlanifieesParCle[cle] = liste;
            }
            liste.Add(jour);
        }

        // Corrections indexées par clé matricule pour les salariés retenus
        var correctionsParCle = corrections
            .Where(h => h.DatePointageIn.HasValue)
            .GroupBy(h => MatriculeIdentity.Cle(h.MatriculeSage), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var resultats = new List<AbsenceReportRow>();

        foreach (var kvp in cartesParCle)
        {
            var cle = kvp.Key;
            var carte = kvp.Value;
            var matriculeAffiche = !string.IsNullOrWhiteSpace(carte.SageMatricule) ? carte.SageMatricule.Trim() : cle;
            var nomAffiche = carte.SageNom?.Trim();
            var prenomAffiche = carte.SagePrenom?.Trim();
            var deptAffiche = carte.Departement?.Trim();

            // Corrections du salarié
            correctionsParCle.TryGetValue(cle, out var corrsSalarie);
            corrsSalarie ??= new List<HeureCorrigee>();
            var corrsParJour = corrsSalarie
                .Where(h => h.DatePointageIn.HasValue)
                .GroupBy(h => h.DatePointageIn!.Value.Date)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Congés SAGE du salarié (C12 : gestion de plusieurs LeaveDay le même jour)
            congesParCle.TryGetValue(cle, out var congesSalarie);
            congesSalarie ??= new List<LeaveDay>();
            var congesParJour = congesSalarie
                .GroupBy(c => c.Date.Date)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var valeurTotale = Math.Min(1m, g.Sum(c => c.Valeur));
                        var codes = g.Select(c => c.Code?.Trim())
                            .Where(c => !string.IsNullOrEmpty(c))
                            .Distinct(StringComparer.OrdinalIgnoreCase);
                        var motif = string.Join(", ", codes);
                        return (Valeur: valeurTotale, Motif: string.IsNullOrEmpty(motif) ? null : motif);
                    });

            // Dates candidates pour ce salarié
            var datesCandidates = new HashSet<DateTime>();

            // 1. Congés SAGE dans [debut, fin]
            foreach (var c in congesSalarie)
            {
                var j = c.Date.Date;
                if (j >= debut && j <= fin)
                    datesCandidates.Add(j);
            }

            // 2. Corrections arbitrées GTA dans [debut, fin]
            foreach (var h in corrsSalarie)
            {
                if (h.DatePointageIn is DateTime d && (h.AbsenceAm == true || h.AbsencePm == true))
                {
                    var j = d.Date;
                    if (j >= debut && j <= fin)
                        datesCandidates.Add(j);
                }
            }

            // 3. Plannings travaillés du salarié dans [debut, fin]
            if (datesPlanifieesParCle.TryGetValue(cle, out var datesPlan))
            {
                foreach (var j in datesPlan)
                    datesCandidates.Add(j);
            }

            foreach (var jour in datesCandidates.OrderBy(d => d))
            {
                // C3 : Pour TOUS les types, week-ends et fériés sont exclus
                var estNonTravaille = EstNonTravaille(jour, feries);
                if (estNonTravaille)
                    continue;

                planningsParCleEtDate.TryGetValue((cle, jour), out var planDuJour);
                var estConge = congesParJour.TryGetValue(jour, out var conge);

                // Priorité 0 : jour de repos planifié (planning OFF) -> aucune ligne, quel que soit
                // le type. Un repos n'est ni une absence ni une journée attendue.
                if (EstJourOff(planDuJour))
                    continue;

                // Priorité 1 : Congé SAGE (C12 : somme des valeurs)
                if (estConge)
                {
                    var periode = conge.Valeur >= 1m ? "Journée" : "Demi-journée";
                    var dureeTheorique = CalculerDureeHeures(planDuJour);
                    if (dureeTheorique.HasValue)
                        dureeTheorique = Math.Round(dureeTheorique.Value * conge.Valeur, 2);

                    resultats.Add(new AbsenceReportRow(
                        Matricule: matriculeAffiche,
                        Nom: nomAffiche,
                        Prenom: prenomAffiche,
                        Departement: deptAffiche,
                        Date: jour,
                        Periode: periode,
                        DureeHeures: dureeTheorique,
                        Type: TypeCongeSage,
                        Motif: conge.Motif
                    ));
                    continue;
                }

                // Priorité 2 : Arbitrée GTA (C4 : périodes AM/PM)
                if (corrsParJour.TryGetValue(jour, out var corrsDuJour))
                {
                    var arb = corrsDuJour.FirstOrDefault(h => h.AbsenceAm == true || h.AbsencePm == true);
                    if (arb != null)
                    {
                        string periode;
                        if (arb.AbsenceAm == true && arb.AbsencePm == true)
                            periode = "Journée";
                        else if (arb.AbsenceAm == true)
                            periode = "Matin";
                        else if (arb.AbsencePm == true)
                            periode = "Après-midi";
                        else
                            periode = "Journée";

                        var dureeTheorique = CalculerDureeHeures(planDuJour);
                        if (dureeTheorique.HasValue && (periode == "Matin" || periode == "Après-midi"))
                            dureeTheorique = Math.Round(dureeTheorique.Value * 0.5m, 2);

                        resultats.Add(new AbsenceReportRow(
                            Matricule: matriculeAffiche,
                            Nom: nomAffiche,
                            Prenom: prenomAffiche,
                            Departement: deptAffiche,
                            Date: jour,
                            Periode: periode,
                            DureeHeures: dureeTheorique,
                            Type: TypeArbitreeGta,
                            Motif: arb.IntituleAbsence
                        ));
                        continue;
                    }
                }

                // Priorité 3 : Non justifiée (jour planifié avec heures, sans aucun pointage)
                var aPointage = corrsParJour.ContainsKey(jour);
                if (EstNonJustifiee(estNonTravaille: false, estConge: false, planDuJour, aPointage))
                {
                    resultats.Add(new AbsenceReportRow(
                        Matricule: matriculeAffiche,
                        Nom: nomAffiche,
                        Prenom: prenomAffiche,
                        Departement: deptAffiche,
                        Date: jour,
                        Periode: "Journée",
                        DureeHeures: CalculerDureeHeures(planDuJour),
                        Type: TypeNonJustifiee,
                        Motif: null
                    ));
                }
            }
        }

        return resultats
            .OrderBy(r => r.Date)
            .ThenBy(r => r.Matricule, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool EstNonTravaille(DateTime jour, IReadOnlySet<DateTime> feries) =>
        jour.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || feries.Contains(jour.Date);

    /// <summary>Jour de repos planifié : la ligne de planning est explicitement marquée OFF.</summary>
    public static bool EstJourOff(PlanningSalarie? plan) => plan?.OffP == true;

    /// <summary>
    /// Règle partagée avec le tableau de bord : un jour est une absence non justifiée quand il est
    /// ouvré, non férié, non couvert par un congé, réellement planifié avec des heures, et sans
    /// aucune heure corrigée (aucun pointage).
    /// </summary>
    public static bool EstNonJustifiee(
        bool estNonTravaille,
        bool estConge,
        PlanningSalarie? plan,
        bool aPointage)
        => !estNonTravaille
           && !estConge
           && !aPointage
           && !EstJourOff(plan)
           && CalculerDureeHeures(plan) is not null;

    public static decimal? CalculerDureeHeures(PlanningSalarie? plan)
    {
        if (plan == null || !plan.Ha.HasValue || !plan.Hd.HasValue)
            return null;

        var ha = plan.Ha.Value.TimeOfDay;
        var hd = plan.Hd.Value.TimeOfDay;
        var pause = plan.Pause ?? TimeSpan.Zero;

        // C7 & R10 : Shift "Pause" ou données avec HA == HD (ex. 00:00:00) -> null
        if (ha == hd)
            return null;

        TimeSpan duree;
        if (hd < ha)
        {
            // Shift traversant minuit (ex: 22:00 à 06:00)
            duree = (hd + TimeSpan.FromHours(24)) - ha - pause;
        }
        else
        {
            duree = hd - ha - pause;
        }

        if (duree <= TimeSpan.Zero)
            return null;

        return Math.Round((decimal)duree.TotalHours, 2);
    }

    /// <summary>Une ligne de travail prime sur une ligne OFF ; à défaut le plus petit ID gagne.</summary>
    private static bool PrimeSur(PlanningSalarie candidat, PlanningSalarie existant)
    {
        var candidatOff = EstJourOff(candidat);
        var existantOff = EstJourOff(existant);
        if (candidatOff != existantOff)
            return !candidatOff;

        return candidat.Id < existant.Id;
    }
}
