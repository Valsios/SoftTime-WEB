using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

public sealed record AbsenceReportRow(
    string Matricule,
    string? Nom,
    string? Prenom,
    string? Departement,
    DateTime Date,
    string Periode,
    decimal? DureeHeures,
    string Type,
    string? Motif
);

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
        IReadOnlyList<T_PLANNING_SAL> plannings,
        IReadOnlyList<T_CARDPAIE> cartes,
        IReadOnlyList<T_HEURECORRIGER> corrections,
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
        var cartesParCle = new Dictionary<string, T_CARDPAIE>(StringComparer.OrdinalIgnoreCase);

        foreach (var carte in cartes)
        {
            var cle = MatriculeIdentity.Cle(carte.SAGE_MATRICULE);
            if (string.IsNullOrEmpty(cle))
                continue;

            cleParIdSal[carte.ID] = cle;
            if (!cartesParCle.TryGetValue(cle, out var existante) || carte.ID < existante.ID)
                cartesParCle[cle] = carte;
        }

        if (cartesParCle.Count == 0)
            return Array.Empty<AbsenceReportRow>();

        // C11 : plannings de TOUTES les cartes de même clé pris en compte. Une ligne de travail
        // prime sur une ligne OFF, puis le plus petit ID l'emporte (choix déterministe).
        var planningsParCleEtDate = new Dictionary<(string Cle, DateTime Date), T_PLANNING_SAL>();
        foreach (var plan in plannings)
        {
            if (plan.DateP is not DateTime dateP)
                continue;
            if (!cleParIdSal.TryGetValue(plan.IDSAL, out var cle))
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
            .Where(h => h.DATE_POINTAGE_IN.HasValue)
            .GroupBy(h => MatriculeIdentity.Cle(h.MATRICULE_SAGE), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var resultats = new List<AbsenceReportRow>();

        foreach (var kvp in cartesParCle)
        {
            var cle = kvp.Key;
            var carte = kvp.Value;
            var matriculeAffiche = !string.IsNullOrWhiteSpace(carte.SAGE_MATRICULE) ? carte.SAGE_MATRICULE.Trim() : cle;
            var nomAffiche = carte.SAGE_NOM?.Trim();
            var prenomAffiche = carte.SAGE_PRENOM?.Trim();
            var deptAffiche = carte.DEPARTEMENT?.Trim();

            // Corrections du salarié
            correctionsParCle.TryGetValue(cle, out var corrsSalarie);
            corrsSalarie ??= new List<T_HEURECORRIGER>();
            var corrsParJour = corrsSalarie
                .Where(h => h.DATE_POINTAGE_IN.HasValue)
                .GroupBy(h => h.DATE_POINTAGE_IN!.Value.Date)
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
                if (h.DATE_POINTAGE_IN is DateTime d && (h.ABSENCE_AM == true || h.ABSENCE_PM == true))
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
                    var arb = corrsDuJour.FirstOrDefault(h => h.ABSENCE_AM == true || h.ABSENCE_PM == true);
                    if (arb != null)
                    {
                        string periode;
                        if (arb.ABSENCE_AM == true && arb.ABSENCE_PM == true)
                            periode = "Journée";
                        else if (arb.ABSENCE_AM == true)
                            periode = "Matin";
                        else if (arb.ABSENCE_PM == true)
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
                            Motif: arb.INTITULE_ABSENCE
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
    public static bool EstJourOff(T_PLANNING_SAL? plan) => plan?.OffP == true;

    /// <summary>
    /// Règle partagée avec le tableau de bord : un jour est une absence non justifiée quand il est
    /// ouvré, non férié, non couvert par un congé, réellement planifié avec des heures, et sans
    /// aucune heure corrigée (aucun pointage).
    /// </summary>
    public static bool EstNonJustifiee(
        bool estNonTravaille,
        bool estConge,
        T_PLANNING_SAL? plan,
        bool aPointage)
        => !estNonTravaille
           && !estConge
           && !aPointage
           && !EstJourOff(plan)
           && CalculerDureeHeures(plan) is not null;

    public static decimal? CalculerDureeHeures(T_PLANNING_SAL? plan)
    {
        if (plan == null || !plan.HA.HasValue || !plan.HD.HasValue)
            return null;

        var ha = plan.HA.Value.TimeOfDay;
        var hd = plan.HD.Value.TimeOfDay;
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
    private static bool PrimeSur(T_PLANNING_SAL candidat, T_PLANNING_SAL existant)
    {
        var candidatOff = EstJourOff(candidat);
        var existantOff = EstJourOff(existant);
        if (candidatOff != existantOff)
            return !candidatOff;

        return candidat.ID < existant.ID;
    }
}
