using System.Globalization;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

/// <summary>
/// Tableau de bord analytique. Portage de l'endpoint <c>Stats</c> du desktop
/// (<c>DashboardController</c>), seules les trois autres routes de ce contrôleur étant
/// commentées (code mort qui lisait la table brute de la pointeuse).
///
/// Les absences « explicites » et les retards se déduisent des heures corrigées, du planning et
/// des tolérances — exactement les mêmes règles que <see cref="OvertimeService"/> pour la semaine,
/// afin que le tableau de bord ne raconte pas autre chose que la paie.
/// </summary>
public class DashboardService
{
    private readonly IUnitOfWork _uow;
    private readonly TenantConnectionService _tenant;
    private readonly SageLeaveCalendar _leaves;

    public DashboardService(IUnitOfWork uow, TenantConnectionService tenant, SageLeaveCalendar leaves)
    {
        _uow = uow;
        _tenant = tenant;
        _leaves = leaves;
    }

    public async Task<DashboardStatsDto> GetStatsAsync(
        PeriodRequest filter, string? departement, string? categorie, string periode, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var from = filter.From.Date;
        var to = filter.To.Date;

        var categorieFiltre = int.TryParse(categorie, out var idCategorie) ? idCategorie : (int?)null;

        // T_CAT_SAL.MATRICULE_SAGE référence en réalité T_CARDPAIE.ID et ID_CATEG pointe vers
        // T_CATEGORIE : c'est le seul lien carte -> catégorie de personnel.
        var categorieParCarte = (await _uow.Repository<T_CAT_SAL>().ListAsync(a => a.BDD_SAGE == sageDb, ct))
            .GroupBy(a => a.MATRICULE_SAGE)
            .ToDictionary(g => g.Key, g => g.First().ID_CATEG);
        var categoriesParId = (await _uow.Repository<T_CATEGORIE>().ListAsync(_ => true, ct))
            .GroupBy(c => c.IDCATEGORIE)
            .ToDictionary(g => g.Key, g => g.First());

        var cartes = (await _uow.Repository<T_CARDPAIE>().ListAsync(c => c.SAGE_BDD == sageDb, ct))
            .Where(c => MatriculeRange.Contains(c.SAGE_MATRICULE, filter))
            .Where(c => string.IsNullOrWhiteSpace(departement)
                        || string.Equals(c.DEPARTEMENT, departement, StringComparison.OrdinalIgnoreCase))
            .Where(c => categorieFiltre is not int idCat
                        || (categorieParCarte.TryGetValue(c.ID, out var carteCat) && carteCat == idCat))
            .ToList();

        if (cartes.Count == 0)
            return Vide(await BuildKpiAsync(sageDb, from, to, 0, VideMetrics, ct));

        var cles = cartes
            .GroupBy(c => Cle(c.SAGE_MATRICULE), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var categorieParCle = cartes
            .GroupBy(c => Cle(c.SAGE_MATRICULE), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => LibelleCategorie(g.First(), categorieParCarte, categoriesParId),
                StringComparer.OrdinalIgnoreCase);
        var feries = (await _uow.Repository<T_FERIE>().ListAsync(f => f.BDD_SAGE == sageDb, ct))
            .Where(f => f.DATE.HasValue)
            .Select(f => f.DATE!.Value.Date)
            .ToHashSet();
        // Congés SAGE du tenant, charges une seule fois pour toute la periode (LOT 9).
        var congesParCle = await _leaves.LoadAsync(ct);

        var corrections = await _uow.Repository<T_HEURECORRIGER>().ListAsync(
            h => h.BDD_SAGE == sageDb && h.DATE_POINTAGE_IN >= from && h.DATE_POINTAGE_IN <= to, ct);
        var correctionsParCle = corrections
            .Where(h => cles.ContainsKey(Cle(h.MATRICULE_SAGE)))
            .GroupBy(h => Cle(h.MATRICULE_SAGE), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var planning = (await _uow.Repository<T_PLANNING_SAL>().ListAsync(
                p => p.BDD_SAGE == sageDb && p.DateP >= from && p.DateP <= to, ct))
            .Where(p => p.DateP.HasValue)
            .ToLookup(p => p.IDSAL);

        var tolerances = await _uow.Repository<T_TOLERANCE>().ListAsync(_ => true, ct);

        // Heures validées de la période, ramenées à la carte pour se plier aux filtres de la page.
        var semaines = (await _uow.Repository<T_HS_VALIDER>().ListAsync(
                v => v.BDD_SAGE == sageDb && v.DATE_DEB >= from && v.DATE_DEB <= to, ct))
            .Where(v => v.DATE_DEB.HasValue && cles.ContainsKey(Cle(v.MATRICULES)))
            .Select(v => new SemaineHs(Cle(v.MATRICULES), v.DATE_DEB!.Value, v.HT ?? 0, v.HS ?? 0, v.VALIDATION_HS == true))
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
            var cle = Cle(carte.SAGE_MATRICULE);
            correctionsParCle.TryGetValue(cle, out var lignes);
            lignes ??= new List<T_HEURECORRIGER>();
            var conges = congesParCle.TryGetValue(cle, out var joursConge) ? joursConge : new List<LeaveDay>();

            // Absences explicites : l'absence est portée par l'heure corrigée.
            foreach (var ligne in lignes.Where(h => h.ABSENCE_AM == true || h.ABSENCE_PM == true))
            {
                if (ligne.DATE_POINTAGE_IN is not DateTime jour || EstNonTravaille(jour, feries))
                    continue;
                absences.Add((cle, jour.Date));
            }

            foreach (var jour in planning[carte.ID]
                         .Where(p => p.OffP != true && p.DateP.HasValue)
                         .Select(p => p.DateP!.Value.Date))
            {
                // LOT 9 : un jour de conge SAGE n'est ni une presence theorique ni une absence, comme
                // dans la validation semaine — il sort donc aussi du denominateur de l'absenteisme.
                if (!DashboardRules.CompteDansPresenceTheorique(EstNonTravaille(jour, feries), LeaveRules.EstConge(conges, jour)))
                    continue;
                joursPlanifies++;
                // Jour planifie sans heure corrigee = absence probable.
                if (!lignes.Any(h => h.DATE_POINTAGE_IN?.Date == jour))
                    absences.Add((cle, jour));
            }

            // Présence : jour réellement pointé (entrée saisie) hors week-end et férié. C'est le
            // dénominateur du taux de ponctualité.
            foreach (var ligne in lignes.Where(h => h.DATE_POINTAGE_IN.HasValue && h.HENTREE.HasValue))
            {
                var jour = ligne.DATE_POINTAGE_IN!.Value.Date;
                if (EstNonTravaille(jour, feries))
                    continue;
                presences.Add((cle, jour));
            }

            // Retards : la valeur saisie prime, sinon règle planning + tolérance d'entrée
            // (identique à OvertimeService.ComputeWeekAsync). On raisonne par journée pour ne pas
            // compter l'entrée d'une deuxième paire (après-midi) comme un retard.
            foreach (var groupeJour in lignes
                         .Where(h => h.DATE_POINTAGE_IN.HasValue)
                         .GroupBy(h => h.DATE_POINTAGE_IN!.Value.Date))
            {
                var jour = groupeJour.Key;
                if (EstNonTravaille(jour, feries))
                    continue;

                var retardsExplicites = groupeJour.Where(h => h.RETARD.HasValue).ToList();
                if (retardsExplicites.Count > 0)
                {
                    var minutes = retardsExplicites
                        .Select(h => (int)h.RETARD!.Value.TotalMinutes)
                        .Max();
                    if (minutes > 0)
                        retards.Add((cle, jour, minutes));
                    continue;
                }

                var entrees = groupeJour
                    .Where(h => h.HENTREE.HasValue)
                    .Select(h => h.HENTREE!.Value)
                    .ToList();
                if (entrees.Count == 0)
                    continue;

                var plan = planning[carte.ID].FirstOrDefault(p => p.DateP?.Date == jour);
                if (plan?.HA is null)
                    continue;
                var tolerance = ToleranceEntree(carte.ID, categorieParCarte, tolerances);
                var autorise = plan.HA.Value.TimeOfDay.Add(TimeSpan.FromMinutes(tolerance));
                var premiereEntree = entrees.Min();
                if (premiereEntree > autorise)
                    retards.Add((cle, jour, (int)(premiereEntree - autorise).TotalMinutes));
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
            && l.Any(h => h.DATE_POINTAGE_IN?.Date == a.Date && (h.ABSENCE_AM == true || h.ABSENCE_PM == true)));

        var series = periodes
            .Select(p => heuresParCle.TryGetValue(p.Cle, out var heures)
                ? new DashboardSerieDto(p.Libelle, heures.Ht, heures.Hs)
                : new DashboardSerieDto(p.Libelle, 0m, 0m))
            .ToList();

        var kpi = await BuildKpiAsync(
            sageDb, from, to, cartes.Count(c => c.ACTIF),
            new PeriodeMetrics(joursPlanifies, joursPresence.Count, joursAbsence.Count, joursRetard.Count,
                joursRetard.Sum(r => r.Minutes), semaines),
            ct);

        var repartition = new DashboardRepartitionDto(
            Repartition(cartes, c => categorieParCle.GetValueOrDefault(Cle(c.SAGE_MATRICULE)), joursAbsence, joursRetard, hsParCle),
            Repartition(cartes, c => c.BRANCHE, joursAbsence, joursRetard, hsParCle),
            Repartition(cartes, c => c.DEPARTEMENT, joursAbsence, joursRetard, hsParCle));

        return new DashboardStatsDto(
            cartes.Count,
            kpi,
            new DashboardAbsencesDto(joursAbsence.Count, explicites, joursAbsence.Count - explicites, statsAbsences, topAbsences),
            new DashboardRetardsDto(joursRetard.Count, joursRetard.Sum(r => r.Minutes), statsRetards, topRetards),
            series,
            repartition,
            Assiduite(cles, categorieParCle, joursPresence, joursAbsence, joursRetard));
    }

    /// <summary>Valeurs distinctes proposées par les filtres de la page (département, branche, catégorie).</summary>
    public async Task<DashboardFiltersDto> GetFiltersAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;

        var cartes = await _uow.Repository<T_CARDPAIE>().ListAsync(c => c.SAGE_BDD == sageDb, ct);

        var categorieParCarte = (await _uow.Repository<T_CAT_SAL>().ListAsync(a => a.BDD_SAGE == sageDb, ct))
            .GroupBy(a => a.MATRICULE_SAGE)
            .ToDictionary(g => g.Key, g => g.First().ID_CATEG);
        var categoriesParId = (await _uow.Repository<T_CATEGORIE>().ListAsync(_ => true, ct))
            .GroupBy(c => c.IDCATEGORIE)
            .ToDictionary(g => g.Key, g => g.First());

        var categories = cartes
            .Select(c => categorieParCarte.TryGetValue(c.ID, out var idCat) && categoriesParId.TryGetValue(idCat, out var cat)
                ? new DashboardOptionDto(cat.IDCATEGORIE, string.IsNullOrWhiteSpace(cat.INTITULE) ? $"Catégorie {cat.IDCATEGORIE}" : cat.INTITULE!)
                : null)
            .Where(o => o is not null)
            .GroupBy(o => o!.Id)
            .Select(g => g.First()!)
            .OrderBy(o => o.Libelle, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new DashboardFiltersDto(
            Valeurs(cartes.Select(c => c.DEPARTEMENT)),
            Valeurs(cartes.Select(c => c.BRANCHE)),
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
        var anomalies = (await _uow.Repository<T_HEUREANOMALIE>().ListAsync(
            a => a.BDD_SAGE == sageDb && a.DATE_POINTAGE_IN >= from && a.DATE_POINTAGE_IN <= to, ct)).Count;
        var corrections = await _uow.Repository<T_HEURECORRIGER>().ListAsync(
            h => h.BDD_SAGE == sageDb && h.DATE_POINTAGE_IN >= from && h.DATE_POINTAGE_IN <= to, ct);
        var manuelles = corrections.Count(h =>
            string.Equals(h.ORIGINE, PunchProcessingConfiguration.OrigineManuel, StringComparison.OrdinalIgnoreCase));

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
    /// Effectif, absences et retards d'un axe (catégorie, branche, département).
    /// Seules les cartes actives sont comptées : c'est l'« effectif actif » annoncé par les KPI,
    /// et les événements sont donc restreints aux mêmes salariés pour que la ligne reste cohérente.
    /// </summary>
    private static List<DashboardSliceDto> Repartition(
        IReadOnlyCollection<T_CARDPAIE> cartes,
        Func<T_CARDPAIE, string?> axe,
        IReadOnlyList<(string Cle, DateTime Date)> absences,
        IReadOnlyList<(string Cle, DateTime Date, int Minutes)> retards,
        IReadOnlyDictionary<string, decimal> hsParCle) => cartes
        .Where(c => c.ACTIF)
        .GroupBy(c => Normaliser(axe(c)), StringComparer.OrdinalIgnoreCase)
        .Select(g =>
        {
            var clesGroupe = g.Select(c => Cle(c.SAGE_MATRICULE)).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
        IReadOnlyDictionary<string, T_CARDPAIE> cles,
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
                    kv.Value.SAGE_NOM,
                    kv.Value.SAGE_PRENOM,
                    kv.Value.DEPARTEMENT,
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
        T_CARDPAIE carte,
        IReadOnlyDictionary<int, int> categorieParCarte,
        IReadOnlyDictionary<int, T_CATEGORIE> categories) =>
        categorieParCarte.TryGetValue(carte.ID, out var idCat) && categories.TryGetValue(idCat, out var categorie)
            ? categorie.INTITULE
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

    /// <summary>Tolérance d'entrée de la catégorie du salarié (TYPES_TOL = false), comme la semaine.</summary>
    private static int ToleranceEntree(
        int carteId,
        IReadOnlyDictionary<int, int> categorieParCarte,
        IReadOnlyList<T_TOLERANCE> tolerances)
    {
        if (!categorieParCarte.TryGetValue(carteId, out var idCategorie))
            return 0;
        return tolerances
            .FirstOrDefault(t => t.IDCATEGORIE == idCategorie && t.TYPES_TOL == false)?.TOLERANCE ?? 0;
    }

    private static DashboardTopDto ToTop(
        string cle, int count, int minutes, IReadOnlyDictionary<string, T_CARDPAIE> cles)
    {
        cles.TryGetValue(cle, out var carte);
        return new DashboardTopDto(cle, carte?.SAGE_NOM, carte?.SAGE_PRENOM, carte?.DEPARTEMENT, count, minutes);
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
