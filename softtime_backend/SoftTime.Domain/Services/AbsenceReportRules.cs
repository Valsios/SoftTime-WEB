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

        var (cleParIdSal, cartesParCle) = IndexerCartes(cartes);
        if (cartesParCle.Count == 0)
            return Array.Empty<AbsenceReportRow>();

        var calendrier = new ReportCalendar(debut, fin, feries, IndexerPlannings(plannings, cleParIdSal, debut, fin));
        var datesPlanifieesParCle = DatesPlanifieesParCle(calendrier.Plannings);
        var correctionsParCle = corrections
            .Where(h => h.DatePointageIn.HasValue)
            .GroupBy(h => MatriculeIdentity.Cle(h.MatriculeSage), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        return cartesParCle
            .SelectMany(kvp => new AbsenceReportEmployee(
                    kvp.Key,
                    kvp.Value,
                    correctionsParCle.GetValueOrDefault(kvp.Key) ?? new List<HeureCorrigee>(),
                    congesParCle.GetValueOrDefault(kvp.Key) ?? new List<LeaveDay>())
                .Lignes(calendrier, datesPlanifieesParCle.GetValueOrDefault(kvp.Key) ?? new List<DateTime>()))
            .OrderBy(r => r.Date)
            .ThenBy(r => r.Matricule, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// C11 : toutes les cartes de même clé matricule sont mappées par leur IDSAL ; la carte de
    /// plus petit ID représente le salarié.
    /// </summary>
    internal static (Dictionary<int, string> CleParIdSal, Dictionary<string, CardPaie> CartesParCle) IndexerCartes(
        IReadOnlyList<CardPaie> cartes)
    {
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
        return (cleParIdSal, cartesParCle);
    }

    /// <summary>
    /// C11 : plannings de toutes les cartes de même clé pris en compte. Une ligne de travail
    /// prime sur une ligne OFF, puis le plus petit ID l'emporte (choix déterministe).
    /// </summary>
    internal static Dictionary<(string Cle, DateTime Date), PlanningSalarie> IndexerPlannings(
        IReadOnlyList<PlanningSalarie> plannings, Dictionary<int, string> cleParIdSal, DateTime debut, DateTime fin)
    {
        var parCleEtDate = new Dictionary<(string Cle, DateTime Date), PlanningSalarie>();
        foreach (var plan in plannings)
        {
            if (plan.DateP is not DateTime dateP || !cleParIdSal.TryGetValue(plan.IdSal, out var cle))
                continue;

            var jour = dateP.Date;
            if (jour < debut || jour > fin)
                continue;

            var key = (cle, jour);
            if (!parCleEtDate.TryGetValue(key, out var existant) || PrimeSur(plan, existant))
                parCleEtDate[key] = plan;
        }
        return parCleEtDate;
    }

    /// <summary>
    /// C10 (performance) : dates réellement planifiées (jour travaillé avec heures) précalculées une
    /// seule fois. Un jour OFF ou sans heures n'est jamais une absence.
    /// </summary>
    private static Dictionary<string, List<DateTime>> DatesPlanifieesParCle(
        IReadOnlyDictionary<(string Cle, DateTime Date), PlanningSalarie> plannings) =>
        plannings
            .Where(kvp => !EstJourOff(kvp.Value) && CalculerDureeHeures(kvp.Value) is not null)
            .GroupBy(kvp => kvp.Key.Cle, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(kvp => kvp.Key.Date).ToList(), StringComparer.OrdinalIgnoreCase);

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
