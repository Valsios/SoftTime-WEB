using System.Globalization;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
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

    public DashboardService(IUnitOfWork uow, TenantConnectionService tenant)
    {
        _uow = uow;
        _tenant = tenant;
    }

    public async Task<DashboardStatsDto> GetStatsAsync(
        PeriodRequest filter, string? departement, string periode, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var from = filter.From.Date;
        var to = filter.To.Date;

        var cartes = (await _uow.Repository<T_CARDPAIE>().ListAsync(c => c.SAGE_BDD == sageDb, ct))
            .Where(c => MatriculeRange.Contains(c.SAGE_MATRICULE, filter))
            .Where(c => string.IsNullOrWhiteSpace(departement)
                        || string.Equals(c.DEPARTEMENT, departement, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var kpi = await BuildKpiAsync(sageDb, from, to, ct);
        if (cartes.Count == 0)
            return new DashboardStatsDto(0, kpi,
                new DashboardAbsencesDto(0, 0, 0, Array.Empty<DashboardGroupDto>(), Array.Empty<DashboardTopDto>()),
                new DashboardRetardsDto(0, 0, Array.Empty<DashboardGroupDto>(), Array.Empty<DashboardTopDto>()));

        var cles = cartes.ToDictionary(c => Cle(c.SAGE_MATRICULE), c => c, StringComparer.OrdinalIgnoreCase);
        var feries = (await _uow.Repository<T_FERIE>().ListAsync(f => f.BDD_SAGE == sageDb, ct))
            .Where(f => f.DATE.HasValue)
            .Select(f => f.DATE!.Value.Date)
            .ToHashSet();

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

        var affectations = (await _uow.Repository<T_CAT_SAL>().ListAsync(a => a.BDD_SAGE == sageDb, ct))
            .ToLookup(a => a.MATRICULE_SAGE);
        var tolerances = await _uow.Repository<T_TOLERANCE>().ListAsync(_ => true, ct);

        var absences = new List<(string Cle, DateTime Date)>();
        var retards = new List<(string Cle, DateTime Date, int Minutes)>();

        foreach (var carte in cartes)
        {
            var cle = Cle(carte.SAGE_MATRICULE);
            correctionsParCle.TryGetValue(cle, out var lignes);
            lignes ??= new List<T_HEURECORRIGER>();

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
                if (EstNonTravaille(jour, feries))
                    continue;
                // Jour planifié sans heure corrigée = absence probable (les congés SAGE ne sont pas
                // encore exclus de ce compteur, cf. LOT 9).
                if (!lignes.Any(h => h.DATE_POINTAGE_IN?.Date == jour))
                    absences.Add((cle, jour));
            }

            // Retards : la valeur saisie prime, sinon règle planning + tolérance d'entrée
            // (identique à OvertimeService.ComputeWeekAsync).
            foreach (var ligne in lignes.Where(h => h.DATE_POINTAGE_IN.HasValue))
            {
                var jour = ligne.DATE_POINTAGE_IN!.Value.Date;
                if (EstNonTravaille(jour, feries))
                    continue;

                if (ligne.RETARD.HasValue)
                {
                    var minutes = (int)ligne.RETARD.Value.TotalMinutes;
                    if (minutes > 0)
                        retards.Add((cle, jour, minutes));
                    continue;
                }

                if (ligne.HENTREE is null)
                    continue;
                var plan = planning[carte.ID].FirstOrDefault(p => p.DateP?.Date == jour);
                if (plan?.HA is null)
                    continue;
                var tolerance = ToleranceEntree(carte, affectations, tolerances);
                var autorise = plan.HA.Value.TimeOfDay.Add(TimeSpan.FromMinutes(tolerance));
                if (ligne.HENTREE.Value > autorise)
                    retards.Add((cle, jour, (int)(ligne.HENTREE.Value - autorise).TotalMinutes));
            }
        }

        var statsAbsences = absences
            .GroupBy(a => PeriodGrouping.Grouper(a.Date, periode))
            .OrderBy(g => g.Key.Cle)
            .Select(g => new DashboardGroupDto(g.Key.Libelle, g.Count(), 0))
            .ToList();
        var statsRetards = retards
            .GroupBy(r => PeriodGrouping.Grouper(r.Date, periode))
            .OrderBy(g => g.Key.Cle)
            .Select(g => new DashboardGroupDto(g.Key.Libelle, g.Count(), g.Sum(r => r.Minutes)))
            .ToList();

        var topAbsences = absences
            .GroupBy(a => a.Cle, StringComparer.OrdinalIgnoreCase)
            .Select(g => ToTop(g.Key, g.Count(), 0, cles))
            .OrderByDescending(t => t.Count)
            .Take(5)
            .ToList();
        var topRetards = retards
            .GroupBy(r => r.Cle, StringComparer.OrdinalIgnoreCase)
            .Select(g => ToTop(g.Key, g.Count(), g.Sum(r => r.Minutes), cles))
            .OrderByDescending(t => t.TotalMinutes)
            .Take(5)
            .ToList();

        var explicites = absences.Count(a => correctionsParCle.TryGetValue(a.Cle, out var l)
            && l.Any(h => h.DATE_POINTAGE_IN?.Date == a.Date && (h.ABSENCE_AM == true || h.ABSENCE_PM == true)));

        return new DashboardStatsDto(
            cartes.Count,
            kpi,
            new DashboardAbsencesDto(absences.Count, explicites, absences.Count - explicites, statsAbsences, topAbsences),
            new DashboardRetardsDto(retards.Count, retards.Sum(r => r.Minutes), statsRetards, topRetards));
    }

    private async Task<DashboardKpiDto> BuildKpiAsync(string sageDb, DateTime from, DateTime to, CancellationToken ct)
    {
        var cartes = await _uow.Repository<T_CARDPAIE>().ListAsync(c => c.SAGE_BDD == sageDb, ct);
        var anomalies = (await _uow.Repository<T_HEUREANOMALIE>().ListAsync(
            a => a.BDD_SAGE == sageDb && a.DATE_POINTAGE_IN >= from && a.DATE_POINTAGE_IN <= to, ct)).Count;
        var corrections = await _uow.Repository<T_HEURECORRIGER>().ListAsync(
            h => h.BDD_SAGE == sageDb && h.DATE_POINTAGE_IN >= from && h.DATE_POINTAGE_IN <= to, ct);
        var manuelles = corrections.Count(h =>
            string.Equals(h.ORIGINE, PunchProcessingConfiguration.OrigineManuel, StringComparison.OrdinalIgnoreCase));

        var semaines = await _uow.Repository<T_HS_VALIDER>().ListAsync(
            v => v.BDD_SAGE == sageDb && v.DATE_DEB >= from && v.DATE_DEB <= to, ct);
        var validees = semaines.Count(v => v.VALIDATION_HS == true);

        return new DashboardKpiDto(
            cartes.Count(c => c.ACTIF),
            anomalies,
            manuelles,
            validees,
            semaines.Count - validees,
            semaines.Sum(v => v.HT ?? 0),
            semaines.Sum(v => v.HS ?? 0));
    }

    /// <summary>Tolérance d'entrée de la catégorie du salarié (TYPES_TOL = false), comme la semaine.</summary>
    private static int ToleranceEntree(
        T_CARDPAIE carte,
        ILookup<int, T_CAT_SAL> affectations,
        IReadOnlyList<T_TOLERANCE> tolerances)
    {
        var affectation = affectations[carte.ID].FirstOrDefault();
        if (affectation is null)
            return 0;
        return tolerances
            .FirstOrDefault(t => t.IDCATEGORIE == affectation.ID_CATEG && t.TYPES_TOL == false)?.TOLERANCE ?? 0;
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
