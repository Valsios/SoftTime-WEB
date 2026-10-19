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
        DateTime dateDuJour,
        DateTime? dernierJourTraite,
        IReadOnlyList<T_PLANNING>? shifts = null)
    {
        var debut = from.Date;
        var finDemande = to.Date;
        var hier = dateDuJour.Date.AddDays(-1);

        // Dictionnaire des shifts connus (pour distinguer les shifts trouvés sans heures / repos)
        var shiftsConnus = shifts?
            .Where(s => s.NoSHIFT.HasValue)
            .GroupBy(s => s.NoSHIFT!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        // Plafond général : aucun jour futur, jamais après hier
        var finGenerale = finDemande < hier ? finDemande : hier;
        if (debut > finGenerale)
            return Array.Empty<AbsenceReportRow>();

        // Plafond pour "Non justifiée" : jamais après dernierJourTraite (C2 & C3)
        // Si dernierJourTraite est null, aucune absence non justifiée n'est produite
        DateTime? finNonJustifiee = dernierJourTraite.HasValue
            ? (finGenerale < dernierJourTraite.Value.Date ? finGenerale : dernierJourTraite.Value.Date)
            : null;

        // C11 : Toutes les cartes de même clé matricule sont mappées par leur IDSAL
        var cleParIdSal = new Dictionary<int, string>();
        var cartesParCle = new Dictionary<string, T_CARDPAIE>(StringComparer.OrdinalIgnoreCase);

        foreach (var carte in cartes)
        {
            var cle = MatriculeIdentity.Cle(carte.SAGE_MATRICULE);
            if (string.IsNullOrEmpty(cle))
                continue;

            cleParIdSal[carte.ID] = cle;
            if (!cartesParCle.ContainsKey(cle))
                cartesParCle[cle] = carte;
        }

        if (cartesParCle.Count == 0)
            return Array.Empty<AbsenceReportRow>();

        // C11 : Plannings de TOUTES les cartes de même clé matricule pris en compte.
        // Si deux plannings tombent le même (clé, jour), conservation déterministe du plus petit ID.
        var planningsParCleEtDate = new Dictionary<(string Cle, DateTime Date), T_PLANNING_SAL>();
        foreach (var p in plannings)
        {
            if (p.DateP is not DateTime dateP || p.OffP == true)
                continue;
            if (!cleParIdSal.TryGetValue(p.IDSAL, out var cle))
                continue;

            var jour = dateP.Date;
            if (jour >= debut && jour <= finGenerale)
            {
                var key = (cle, jour);
                if (!planningsParCleEtDate.TryGetValue(key, out var existant) || p.ID < existant.ID)
                {
                    planningsParCleEtDate[key] = p;
                }
            }
        }

        // C10 (PERFORMANCE) : Dictionnaire clé-matricule -> dates planifiées précalculé UNE fois avant la boucle
        var datesPlanifieesParCle = new Dictionary<string, List<DateTime>>(StringComparer.OrdinalIgnoreCase);
        if (finNonJustifiee.HasValue)
        {
            foreach (var kvp in planningsParCleEtDate)
            {
                var (cle, jour) = kvp.Key;
                if (jour >= debut && jour <= finNonJustifiee.Value)
                {
                    if (!datesPlanifieesParCle.TryGetValue(cle, out var liste))
                    {
                        liste = new List<DateTime>();
                        datesPlanifieesParCle[cle] = liste;
                    }
                    liste.Add(jour);
                }
            }
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

            // 1. Congés SAGE dans [debut, finGenerale]
            foreach (var c in congesSalarie)
            {
                var j = c.Date.Date;
                if (j >= debut && j <= finGenerale)
                    datesCandidates.Add(j);
            }

            // 2. Corrections arbitrées GTA dans [debut, finGenerale]
            foreach (var h in corrsSalarie)
            {
                if (h.DATE_POINTAGE_IN is DateTime d && (h.ABSENCE_AM == true || h.ABSENCE_PM == true))
                {
                    var j = d.Date;
                    if (j >= debut && j <= finGenerale)
                        datesCandidates.Add(j);
                }
            }

            // 3. Plannings du salarié dans [debut, finNonJustifiee] via dictionnaire précalculé (C10)
            if (datesPlanifieesParCle.TryGetValue(cle, out var datesPlan))
            {
                foreach (var j in datesPlan)
                    datesCandidates.Add(j);
            }

            foreach (var jour in datesCandidates.OrderBy(d => d))
            {
                // C3 : Pour TOUS les types, week-ends et fériés sont exclus
                if (EstNonTravaille(jour, feries))
                    continue;

                planningsParCleEtDate.TryGetValue((cle, jour), out var planDuJour);

                // Priorité 1 : Congé SAGE (C3 : jour <= finGenerale ; C12 : somme des valeurs)
                if (congesParJour.TryGetValue(jour, out var conge))
                {
                    var periode = conge.Valeur >= 1m ? "Journée" : "Demi-journée";
                    var dureeTheorique = CalculerDureeHeures(planDuJour);
                    if (dureeTheorique.HasValue)
                    {
                        dureeTheorique = Math.Round(dureeTheorique.Value * conge.Valeur, 2);
                    }

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

                // Priorité 2 : Arbitrée GTA (C3 : jour <= finGenerale ; C4 : périodes AM/PM)
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
                        {
                            dureeTheorique = Math.Round(dureeTheorique.Value * 0.5m, 2);
                        }

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

                // Priorité 3 : Non justifiée (C2 & C3 : jour <= finNonJustifiee)
                if (finNonJustifiee.HasValue && jour <= finNonJustifiee.Value && planDuJour != null)
                {
                    var aPointageOuCorrection = corrsParJour.ContainsKey(jour);
                    if (!aPointageOuCorrection)
                    {
                        // Règle repos : si le planning référence un shift trouvé dont HA et HD sont NULL,
                        // c'est un jour de repos planifié (ex: Pause) -> ne pas générer de ligne "Non justifiée".
                        // Si le shift est introuvable dans T_PLANNING, la ligne est conservée (durée nulle).
                        var estReposPlanifie = planDuJour.NoSHIFT.HasValue
                            && shiftsConnus != null
                            && shiftsConnus.TryGetValue(planDuJour.NoSHIFT.Value, out var defShift)
                            && defShift.HA == null && defShift.HD == null;

                        if (!estReposPlanifie)
                        {
                            var dureeTheorique = CalculerDureeHeures(planDuJour);
                            resultats.Add(new AbsenceReportRow(
                                Matricule: matriculeAffiche,
                                Nom: nomAffiche,
                                Prenom: prenomAffiche,
                                Departement: deptAffiche,
                                Date: jour,
                                Periode: "Journée",
                                DureeHeures: dureeTheorique,
                                Type: TypeNonJustifiee,
                                Motif: null
                            ));
                        }
                    }
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
}
