using System.Globalization;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

/// <summary>
/// Tableau de bord analytique. Portage de l'endpoint <c>Stats</c> du desktop
/// (<c>DashboardController</c>), seules les trois autres routes de ce contrôleur étant
/// commentées (code mort qui lisait la table brute de la pointeuse).
///
/// Les absences « explicites » et les retards se déduisent des heures corrigées, du planning et
/// des tolérances — exactement les mêmes règles que <see cref="OvertimeService"/> pour la semaine,
/// afin que le tableau de bord ne raconte pas autre chose que la paie.
/// </summary>
public class DashboardService : IDashboardService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly ISageLeaveCalendar _leaves;

    public DashboardService(IUnitOfWork uow, ITenantConnectionService tenant, ISageLeaveCalendar leaves)
    {
        _uow = uow;
        _tenant = tenant;
        _leaves = leaves;
    }

    public async Task<DashboardStatsDto> GetStatsAsync(
        PeriodRequest filter, string? categorie, string periode, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var from = filter.From.Date;
        var to = filter.To.Date;

        // Aucun événement futur : absences et retards sont calculés jusqu'à la veille, exactement
        // comme les états (rapports) qui partagent les mêmes règles.
        var hier = DateTime.Today.AddDays(-1);
        var finEvenements = to < hier ? to : hier;

        var categorieFiltre = int.TryParse(categorie, out var idCategorie) ? idCategorie : (int?)null;

        // T_CAT_SAL.MATRICULE_SAGE référence en réalité T_CARDPAIE.ID et ID_CATEG pointe vers
        // T_CATEGORIE : c'est le seul lien carte -> catégorie de personnel.
        var categorieParCarte = (await _uow.Repository<CategorieSalarie>().ListAsync(a => a.BddSage == sageDb, ct))
            .GroupBy(a => a.MatriculeSage)
            .ToDictionary(g => g.Key, g => g.First().IdCateg);
        var categoriesParId = (await _uow.Repository<Categorie>().ListAsync(_ => true, ct))
            .GroupBy(c => c.IdCategorie)
            .ToDictionary(g => g.Key, g => g.First());

        var cartes = (await _uow.Repository<CardPaie>().ListAsync(c => c.SageBdd == sageDb, ct))
            .Where(c => MatriculeRange.Contains(c.SageMatricule, filter))
            .Where(c => string.IsNullOrWhiteSpace(filter.Departement)
                        || string.Equals(c.Departement, filter.Departement, StringComparison.OrdinalIgnoreCase))
            .Where(c => categorieFiltre is not int idCat
                        || (categorieParCarte.TryGetValue(c.Id, out var carteCat) && carteCat == idCat))
            .ToList();

        if (cartes.Count == 0)
            return Vide(await BuildKpiAsync(sageDb, from, to, 0, VideMetrics, ct));

        var cles = cartes
            .GroupBy(c => Cle(c.SageMatricule), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var categorieParCle = cartes
            .GroupBy(c => Cle(c.SageMatricule), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => LibelleCategorie(g.First(), categorieParCarte, categoriesParId),
                StringComparer.OrdinalIgnoreCase);
        var feries = (await _uow.Repository<JourFerie>().ListAsync(f => f.BddSage == sageDb, ct))
            .Where(f => f.Date.HasValue)
            .Select(f => f.Date!.Value.Date)
            .ToHashSet();
        // Congés SAGE du tenant, charges une seule fois pour toute la periode (LOT 9).
        var congesParCle = await _leaves.LoadAsync(ct);

        var corrections = await _uow.Repository<HeureCorrigee>().ListAsync(
            h => h.BddSage == sageDb && h.DatePointageIn >= from && h.DatePointageIn <= to, ct);
        var correctionsParCle = corrections
            .Where(h => cles.ContainsKey(Cle(h.MatriculeSage)))
            .GroupBy(h => Cle(h.MatriculeSage), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // Planning de chaque carte, indexé par jour : une ligne de travail prime sur une ligne OFF,
        // puis le plus petit ID (même convention déterministe que l'état des absences).
        var planning = (await _uow.Repository<PlanningSalarie>().ListAsync(
                p => p.BddSage == sageDb && p.DateP >= from && p.DateP <= to, ct))
            .Where(p => p.DateP.HasValue)
            .GroupBy(p => p.IdSal)
            .ToDictionary(
                g => g.Key,
                g => g
                    .GroupBy(p => p.DateP!.Value.Date)
                    .ToDictionary(
                        d => d.Key,
                        d => d
                            .OrderBy(p => AbsenceReportRules.EstJourOff(p) ? 1 : 0)
                            .ThenBy(p => p.Id)
                            .First()));

        var tolerances = await _uow.Repository<ToleranceRetard>().ListAsync(_ => true, ct);

        // Heures validées de la période, ramenées à la carte pour se plier aux filtres de la page.
        var semaines = (await _uow.Repository<HsValidee>().ListAsync(
                v => v.BddSage == sageDb && v.DateDeb >= from && v.DateDeb <= to, ct))
            .Where(v => v.DateDeb.HasValue && cles.ContainsKey(Cle(v.Matricules)))
            .Select(v => new SemaineHs(Cle(v.Matricules), v.DateDeb!.Value, v.Ht ?? 0, v.Hs ?? 0, v.ValidationHs == true))
            .ToList();
        var hsParCle = semaines
            .GroupBy(s => s.Cle, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Hs), StringComparer.OrdinalIgnoreCase);

        var absences = new List<(string Cle, DateTime Date)>();
        var retards = new List<(string Cle, DateTime Date, int Minutes)>();
        var presences = new List<(string Cle, DateTime Date)>();
        var joursPlanifies = 0;

        foreach (var carte in cartes)
        {
            var cle = Cle(carte.SageMatricule);
            correctionsParCle.TryGetValue(cle, out var lignes);
            lignes ??= new List<HeureCorrigee>();
            var conges = congesParCle.TryGetValue(cle, out var joursConge) ? joursConge : new List<LeaveDay>();
            var plansDuSalarie = planning.TryGetValue(carte.Id, out var plansCarte) ? plansCarte : PlansVides;

            // Absences explicites : l'absence est portée par l'heure corrigée. Un jour de repos
            // planifié (OFF) n'est jamais une absence, même si une correction le mentionne.
            foreach (var ligne in lignes.Where(h => h.AbsenceAm == true || h.AbsencePm == true))
            {
                if (ligne.DatePointageIn is not DateTime jour)
                    continue;
                var jourDate = jour.Date;
                if (jourDate > finEvenements || EstNonTravaille(jourDate, feries))
                    continue;
                if (AbsenceReportRules.EstJourOff(plansDuSalarie.GetValueOrDefault(jourDate)))
                    continue;
                absences.Add((cle, jourDate));
            }

            foreach (var (jour, plan) in plansDuSalarie)
            {
                if (jour > finEvenements)
                    continue;

                var estNonTravaille = EstNonTravaille(jour, feries);
                var estConge = LeaveRules.EstConge(conges, jour);
                // LOT 9 : un jour de conge SAGE n'est ni une presence theorique ni une absence, comme
                // dans la validation semaine — il sort donc aussi du denominateur de l'absenteisme.
                if (!DashboardRules.CompteDansPresenceTheorique(estNonTravaille, estConge))
                    continue;
                // Jour de repos planifié (OFF) ou shift sans heures : aucune journée attendue.
                if (AbsenceReportRules.CalculerDureeHeures(plan) is null)
                    continue;

                joursPlanifies++;
                // Règle partagée avec l'état des absences : jour planifié avec heures, sans pointage.
                var aPointage = lignes.Any(h => h.DatePointageIn?.Date == jour);
                if (AbsenceReportRules.EstNonJustifiee(estNonTravaille, estConge, plan, aPointage))
                    absences.Add((cle, jour));
            }

            // Présence : jour réellement pointé (entrée saisie) hors week-end et férié. C'est le
            // dénominateur du taux de ponctualité.
            foreach (var ligne in lignes.Where(h => h.DatePointageIn.HasValue && h.HEntree.HasValue))
            {
                var jour = ligne.DatePointageIn!.Value.Date;
                if (jour > finEvenements || EstNonTravaille(jour, feries))
                    continue;
                presences.Add((cle, jour));
            }

            // Retards : règle partagée avec l'état des retards (valeur saisie prioritaire, sinon
            // première entrée du jour vs planning + tolérance d'entrée). On raisonne par journée pour
            // ne pas compter l'entrée d'une deuxième paire (après-midi) comme un retard.
            foreach (var groupeJour in lignes
                         .Where(h => h.DatePointageIn.HasValue)
                         .GroupBy(h => h.DatePointageIn!.Value.Date))
            {
                var jour = groupeJour.Key;
                if (jour > finEvenements || EstNonTravaille(jour, feries))
                    continue;

                var entrees = groupeJour
                    .Where(h => h.HEntree.HasValue)
                    .Select(h => h.HEntree!.Value)
                    .ToList();
                var minutes = RetardReportRules.MinutesRetardJour(
                    absenceMatin: groupeJour.Any(h => h.AbsenceAm == true),
                    retardsSaisis: groupeJour.Select(h => h.Retard),
                    premiereEntree: entrees.Count > 0 ? entrees.Min() : null,
                    heurePrevue: RetardReportRules.HeurePrevue(plansDuSalarie.GetValueOrDefault(jour)),
                    toleranceMinutes: RetardReportRules.ToleranceEntree(carte.Id, categorieParCarte, tolerances));

                if (minutes > 0)
                    retards.Add((cle, jour, minutes));
            }
        }

        // Une journée = une ligne : plusieurs heures corrigées le même jour ne comptent qu'une fois.
        var joursAbsence = absences.Distinct().ToList();
        var joursPresence = presences.Distinct().ToList();
        var joursRetard = retards
            .GroupBy(r => (r.Cle, r.Date))
            .Select(g => (g.Key.Cle, g.Key.Date, Minutes: g.Sum(r => r.Minutes)))
            .ToList();

        // Toutes les périodes de la plage, même celles sans événement : les courbes et les tableaux
        // restent ainsi alignés (une semaine sans absence vaut 0 et n'est pas omise du tableau).
        var periodes = PeriodGrouping.Clefs(from, to, periode);
        var absencesParCle = joursAbsence
            .GroupBy(a => PeriodGrouping.Grouper(a.Date, periode).Cle)
            .ToDictionary(g => g.Key, g => g.Count());
        var retardsParCle = joursRetard
            .GroupBy(r => PeriodGrouping.Grouper(r.Date, periode).Cle)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Minutes: g.Sum(r => r.Minutes)));
        var heuresParCle = semaines
            .GroupBy(s => PeriodGrouping.Grouper(s.DateDeb, periode).Cle)
            .ToDictionary(g => g.Key, g => (Ht: g.Sum(s => s.Ht), Hs: g.Sum(s => s.Hs)));

        var statsAbsences = periodes
            .Select(p => new DashboardGroupDto(p.Libelle, absencesParCle.GetValueOrDefault(p.Cle), 0))
            .ToList();
        var statsRetards = periodes
            .Select(p => new DashboardGroupDto(
                p.Libelle,
                retardsParCle.TryGetValue(p.Cle, out var retard) ? retard.Count : 0,
                retardsParCle.TryGetValue(p.Cle, out var retardMinutes) ? retardMinutes.Minutes : 0))
            .ToList();

        var topAbsences = joursAbsence
            .GroupBy(a => a.Cle, StringComparer.OrdinalIgnoreCase)
            .Select(g => ToTop(g.Key, g.Count(), 0, cles))
            .OrderByDescending(t => t.Count)
            .Take(5)
            .ToList();
        var topRetards = joursRetard
            .GroupBy(r => r.Cle, StringComparer.OrdinalIgnoreCase)
            .Select(g => ToTop(g.Key, g.Count(), g.Sum(r => r.Minutes), cles))
            .OrderByDescending(t => t.TotalMinutes)
            .Take(5)
            .ToList();

        var explicites = joursAbsence.Count(a => correctionsParCle.TryGetValue(a.Cle, out var l)
            && l.Any(h => h.DatePointageIn?.Date == a.Date && (h.AbsenceAm == true || h.AbsencePm == true)));

        var series = periodes
            .Select(p => heuresParCle.TryGetValue(p.Cle, out var heures)
                ? new DashboardSerieDto(p.Libelle, heures.Ht, heures.Hs)
                : new DashboardSerieDto(p.Libelle, 0m, 0m))
            .ToList();

        var kpi = await BuildKpiAsync(
            sageDb, from, to, cartes.Count(c => c.Actif),
            new PeriodeMetrics(joursPlanifies, joursPresence.Count, joursAbsence.Count, joursRetard.Count,
                joursRetard.Sum(r => r.Minutes), semaines),
            ct);

        var repartition = new DashboardRepartitionDto(
            Repartition(cartes, c => categorieParCle.GetValueOrDefault(Cle(c.SageMatricule)), joursAbsence, joursRetard, hsParCle),
            Repartition(cartes, c => c.Departement, joursAbsence, joursRetard, hsParCle));

        return new DashboardStatsDto(
            cartes.Count,
            kpi,
            new DashboardAbsencesDto(joursAbsence.Count, explicites, joursAbsence.Count - explicites, statsAbsences, topAbsences),
            new DashboardRetardsDto(joursRetard.Count, joursRetard.Sum(r => r.Minutes), statsRetards, topRetards),
            series,
            repartition,
            Assiduite(cles, categorieParCle, joursPresence, joursAbsence, joursRetard));
    }

    /// <summary>Valeurs distinctes proposées par les filtres de la page (département, catégorie).</summary>
    public async Task<DashboardFiltersDto> GetFiltersAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;

        var cartes = await _uow.Repository<CardPaie>().ListReadOnlyAsync(c => c.SageBdd == sageDb, ct);

        var categorieParCarte = (await _uow.Repository<CategorieSalarie>().ListReadOnlyAsync(a => a.BddSage == sageDb, ct))
            .GroupBy(a => a.MatriculeSage)
            .ToDictionary(g => g.Key, g => g.First().IdCateg);
        var categoriesParId = (await _uow.Repository<Categorie>().ListReadOnlyAsync(_ => true, ct))
            .GroupBy(c => c.IdCategorie)
            .ToDictionary(g => g.Key, g => g.First());

        var categories = cartes
            .Select(c => categorieParCarte.TryGetValue(c.Id, out var idCat) && categoriesParId.TryGetValue(idCat, out var cat)
                ? new DashboardOptionDto(cat.IdCategorie, string.IsNullOrWhiteSpace(cat.Intitule) ? $"Catégorie {cat.IdCategorie}" : cat.Intitule!)
                : null)
            .Where(o => o is not null)
            .GroupBy(o => o!.Id)
            .Select(g => g.First()!)
            .OrderBy(o => o.Libelle, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new DashboardFiltersDto(
            Valeurs(cartes.Select(c => c.Departement)),
            categories);
    }

    private static List<string> Valeurs(IEnumerable<string?> source) => source
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .Select(v => v!.Trim())
        .Distinct(StringComparer.CurrentCultureIgnoreCase)
        .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    private async Task<DashboardKpiDto> BuildKpiAsync(
        string sageDb, DateTime from, DateTime to, int cartesActives, PeriodeMetrics metrics, CancellationToken ct)
    {
        var anomalies = (await _uow.Repository<HeureAnomalie>().ListAsync(
            a => a.BddSage == sageDb && a.DatePointageIn >= from && a.DatePointageIn <= to, ct)).Count;
        var corrections = await _uow.Repository<HeureCorrigee>().ListAsync(
            h => h.BddSage == sageDb && h.DatePointageIn >= from && h.DatePointageIn <= to, ct);
        var manuelles = corrections.Count(h =>
            string.Equals(h.Origine, PunchProcessingConfiguration.OrigineManuel, StringComparison.OrdinalIgnoreCase));

        var validees = metrics.Semaines.Count(s => s.Validee);

        return new DashboardKpiDto(
            cartesActives,
            anomalies,
            manuelles,
            validees,
            metrics.Semaines.Count - validees,
            metrics.Semaines.Sum(s => s.Ht),
            metrics.Semaines.Sum(s => s.Hs),
            metrics.JoursPlanifies,
            metrics.JoursTravailles,
            metrics.JoursAbsence,
            metrics.JoursRetard,
            metrics.MinutesRetard,
            DashboardRules.TauxAbsenteisme(metrics.JoursAbsence, metrics.JoursPlanifies),
            DashboardRules.TauxPonctualite(metrics.JoursTravailles, metrics.JoursRetard));
    }

    private static DashboardStatsDto Vide(DashboardKpiDto kpi) => new(
        0,
        kpi,
        new DashboardAbsencesDto(0, 0, 0, Array.Empty<DashboardGroupDto>(), Array.Empty<DashboardTopDto>()),
        new DashboardRetardsDto(0, 0, Array.Empty<DashboardGroupDto>(), Array.Empty<DashboardTopDto>()),
        Array.Empty<DashboardSerieDto>(),
        new DashboardRepartitionDto(),
        new DashboardAssiduiteDto());

    private static readonly PeriodeMetrics VideMetrics = new(0, 0, 0, 0, 0, Array.Empty<SemaineHs>());

    /// <summary>
    /// Effectif, absences et retards d'un axe (catégorie, département).
    /// Seules les cartes actives sont comptées : c'est l'« effectif actif » annoncé par les KPI,
    /// et les événements sont donc restreints aux mêmes salariés pour que la ligne reste cohérente.
    /// </summary>
    private static List<DashboardSliceDto> Repartition(
        IReadOnlyCollection<CardPaie> cartes,
        Func<CardPaie, string?> axe,
        IReadOnlyList<(string Cle, DateTime Date)> absences,
        IReadOnlyList<(string Cle, DateTime Date, int Minutes)> retards,
        IReadOnlyDictionary<string, decimal> hsParCle) => cartes
        .Where(c => c.Actif)
        .GroupBy(c => Normaliser(axe(c)), StringComparer.OrdinalIgnoreCase)
        .Select(g =>
        {
            var clesGroupe = g.Select(c => Cle(c.SageMatricule)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return new DashboardSliceDto(
                g.Key,
                g.Count(),
                absences.Count(a => clesGroupe.Contains(a.Cle)),
                retards.Count(r => clesGroupe.Contains(r.Cle)),
                retards.Where(r => clesGroupe.Contains(r.Cle)).Sum(r => r.Minutes),
                hsParCle.Where(kv => clesGroupe.Contains(kv.Key)).Sum(kv => kv.Value));
        })
        .OrderByDescending(s => s.Effectif)
        .ThenBy(s => s.Libelle, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    /// <summary>
    /// Classement d'assiduité : seuls les salariés ayant réellement pointé sont classés (sinon tout
    /// salarié sans pointage apparaîtrait « le plus ponctuel » à 0 jour travaillé).
    /// </summary>
    private static DashboardAssiduiteDto Assiduite(
        IReadOnlyDictionary<string, CardPaie> cles,
        IReadOnlyDictionary<string, string?> categorieParCle,
        IReadOnlyList<(string Cle, DateTime Date)> presences,
        IReadOnlyList<(string Cle, DateTime Date)> absences,
        IReadOnlyList<(string Cle, DateTime Date, int Minutes)> retards)
    {
        var travail = presences.GroupBy(p => p.Cle, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var manques = absences.GroupBy(a => a.Cle, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var retardsParCle = retards.GroupBy(r => r.Cle, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Minutes: g.Sum(r => r.Minutes)), StringComparer.OrdinalIgnoreCase);

        var lignes = cles
            .Where(kv => travail.ContainsKey(kv.Key))
            .Select(kv =>
            {
                var joursTravailles = travail[kv.Key];
                manques.TryGetValue(kv.Key, out var joursAbsence);
                retardsParCle.TryGetValue(kv.Key, out var retard);
                categorieParCle.TryGetValue(kv.Key, out var categorie);
                return new DashboardAssiduiteRowDto(
                    kv.Key,
                    kv.Value.SageNom,
                    kv.Value.SagePrenom,
                    kv.Value.Departement,
                    categorie,
                    joursTravailles,
                    joursAbsence,
                    retard.Count,
                    retard.Minutes,
                    DashboardRules.TauxPonctualite(joursTravailles, retard.Count));
            })
            .ToList();

        var plusPonctuels = lignes
            .OrderByDescending(l => l.TauxPonctualite)
            .ThenBy(l => l.JoursRetard)
            .ThenBy(l => l.MinutesRetard)
            .ThenBy(l => l.JoursAbsence)
            .ThenBy(l => l.Matricule, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        var moinsPonctuels = lignes
            .OrderBy(l => l.TauxPonctualite)
            .ThenByDescending(l => l.MinutesRetard)
            .ThenByDescending(l => l.JoursAbsence)
            .ThenBy(l => l.Matricule, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        return new DashboardAssiduiteDto(plusPonctuels, moinsPonctuels);
    }

    private static string? LibelleCategorie(
        CardPaie carte,
        IReadOnlyDictionary<int, int> categorieParCarte,
        IReadOnlyDictionary<int, Categorie> categories) =>
        categorieParCarte.TryGetValue(carte.Id, out var idCat) && categories.TryGetValue(idCat, out var categorie)
            ? categorie.Intitule
            : null;

    private static string Normaliser(string? valeur) =>
        string.IsNullOrWhiteSpace(valeur) ? "(non défini)" : valeur.Trim();

    /// <summary>Ligne de validation hebdomadaire ramenée au matricule de la carte.</summary>
    private sealed record SemaineHs(string Cle, DateTime DateDeb, decimal Ht, decimal Hs, bool Validee);

    /// <summary>Compteurs de la période, calculés sur les cartes filtrées par la page.</summary>
    private sealed record PeriodeMetrics(
        int JoursPlanifies,
        int JoursTravailles,
        int JoursAbsence,
        int JoursRetard,
        int MinutesRetard,
        IReadOnlyList<SemaineHs> Semaines);

    /// <summary>Planning vide partagé : évite une allocation par salarié sans planning.</summary>
    private static readonly Dictionary<DateTime, PlanningSalarie> PlansVides = new();

    private static DashboardTopDto ToTop(
        string cle, int count, int minutes, IReadOnlyDictionary<string, CardPaie> cles)
    {
        cles.TryGetValue(cle, out var carte);
        return new DashboardTopDto(cle, carte?.SageNom, carte?.SagePrenom, carte?.Departement, count, minutes);
    }

    private static bool EstNonTravaille(DateTime jour, IReadOnlySet<DateTime> feries) =>
        jour.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || feries.Contains(jour.Date);

    /// <summary>Clé de rapprochement insensible au remplissage par des zéros (« 00369 » == « 369 »).</summary>
    private static string Cle(string? matricule)
    {
        var m = matricule?.Trim() ?? string.Empty;
        return long.TryParse(m, out var n) ? n.ToString(CultureInfo.InvariantCulture) : m.ToUpperInvariant();
    }
}
