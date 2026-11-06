using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Infrastructure.Services.Dashboard;

namespace SoftTime.Infrastructure.Services;

public sealed class DashboardService : IDashboardService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly ISageLeaveCalendar _leaves;

    public DashboardService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        ISageLeaveCalendar leaves)
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

        var perimetre = await LoadPerimetreAsync(filter, categorie, ct);
        if (perimetre.Cartes.Count == 0)
            return Vide(await BuildKpiAsync(sageDb, from, to, 0, VideMetrics, ct));

        var data = await LoadDataAsync(perimetre, from, to, ct);
        var collector = new DashboardEventCollector(data);
        foreach (var carte in perimetre.Cartes)
            collector.Collecter(carte);
        var events = collector.Resultat();

        var metrics = new PeriodeMetrics(events.JoursPlanifies, events.Presences.Count, events.Absences.Count,
            events.Retards.Count, events.MinutesRetard, data.Semaines);
        var kpi = await BuildKpiAsync(sageDb, from, to, perimetre.Cartes.Count(c => c.Actif), metrics, ct);
        return BuildStats(data, events, kpi, periode);
    }

    private async Task<DashboardPerimetre> LoadPerimetreAsync(
        PeriodRequest filter, string? categorie, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
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
        return new DashboardPerimetre(cartes, categorieParCarte, categoriesParId);
    }

    private async Task<DashboardData> LoadDataAsync(
        DashboardPerimetre perimetre, DateTime from, DateTime to, CancellationToken ct)
    {
        var cles = perimetre.Cartes
            .GroupBy(c => DashboardIndicators.Cle(c.SageMatricule), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var semaines = await LoadSemainesAsync(cles, from, to, ct);

        // Aucun événement futur : absences et retards sont calculés jusqu'à la veille, exactement
        // comme les états (rapports) qui partagent les mêmes règles.
        var hier = DateTime.Today.AddDays(-1);
        return new DashboardData
        {
            Perimetre = perimetre,
            From = from,
            To = to,
            FinEvenements = to < hier ? to : hier,
            Cles = cles,
            CategorieParCle = cles.ToDictionary(
                kv => kv.Key,
                kv => DashboardIndicators.LibelleCategorie(kv.Value, perimetre.CategorieParCarte, perimetre.CategoriesParId),
                StringComparer.OrdinalIgnoreCase),
            Feries = await LoadFeriesAsync(ct),
            // Congés SAGE du tenant, charges une seule fois pour toute la periode (LOT 9).
            CongesParCle = await _leaves.LoadAsync(ct),
            CorrectionsParCle = await LoadCorrectionsParCleAsync(cles, from, to, ct),
            Planning = await LoadPlanningAsync(from, to, ct),
            Tolerances = await _uow.Repository<ToleranceRetard>().ListAsync(_ => true, ct),
            Semaines = semaines,
            HsParCle = semaines
                .GroupBy(s => s.Cle, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(s => s.Hs), StringComparer.OrdinalIgnoreCase)
        };
    }

    private async Task<HashSet<DateTime>> LoadFeriesAsync(CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        return (await _uow.Repository<JourFerie>().ListAsync(f => f.BddSage == sageDb, ct))
            .Where(f => f.Date.HasValue)
            .Select(f => f.Date!.Value.Date)
            .ToHashSet();
    }

    private async Task<Dictionary<string, List<HeureCorrigee>>> LoadCorrectionsParCleAsync(
        Dictionary<string, CardPaie> cles, DateTime from, DateTime to, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        var corrections = await _uow.Repository<HeureCorrigee>().ListAsync(
            h => h.BddSage == sageDb && h.DatePointageIn >= from && h.DatePointageIn <= to, ct);
        return corrections
            .Where(h => cles.ContainsKey(DashboardIndicators.Cle(h.MatriculeSage)))
            .GroupBy(h => DashboardIndicators.Cle(h.MatriculeSage), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Planning de chaque carte, indexé par jour : une ligne de travail prime sur une ligne OFF,
    /// puis le plus petit ID (même convention déterministe que l'état des absences).
    /// </summary>
    private async Task<Dictionary<int, Dictionary<DateTime, PlanningSalarie>>> LoadPlanningAsync(
        DateTime from, DateTime to, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        return (await _uow.Repository<PlanningSalarie>().ListAsync(
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
    }

    /// <summary>Heures validées de la période, ramenées à la carte pour se plier aux filtres de la page.</summary>
    private async Task<List<SemaineHs>> LoadSemainesAsync(
        Dictionary<string, CardPaie> cles, DateTime from, DateTime to, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        return (await _uow.Repository<HsValidee>().ListAsync(
                v => v.BddSage == sageDb && v.DateDeb >= from && v.DateDeb <= to, ct))
            .Where(v => v.DateDeb.HasValue && cles.ContainsKey(DashboardIndicators.Cle(v.Matricules)))
            .Select(v => new SemaineHs(DashboardIndicators.Cle(v.Matricules), v.DateDeb!.Value, v.Ht ?? 0, v.Hs ?? 0, v.ValidationHs == true))
            .ToList();
    }

    /// <summary>
    /// Toutes les périodes de la plage figurent, même celles sans événement : les courbes et les
    /// tableaux restent ainsi alignés (une semaine sans absence vaut 0 et n'est pas omise).
    /// </summary>
    private static DashboardStatsDto BuildStats(
        DashboardData data, DashboardEvents events, DashboardKpiDto kpi, string periode)
    {
        var periodes = PeriodGrouping.Clefs(data.From, data.To, periode);
        var cartes = data.Perimetre.Cartes;
        var repartition = new DashboardRepartitionDto(
            DashboardIndicators.Repartition(cartes, c => data.CategorieParCle.GetValueOrDefault(DashboardIndicators.Cle(c.SageMatricule)), events.Absences, events.Retards, data.HsParCle),
            DashboardIndicators.Repartition(cartes, c => c.Departement, events.Absences, events.Retards, data.HsParCle));

        return new DashboardStatsDto(
            cartes.Count,
            kpi,
            BuildAbsences(data, events, periodes, periode),
            BuildRetards(data, events, periodes, periode),
            BuildSeries(data.Semaines, periodes, periode),
            repartition,
            DashboardIndicators.Assiduite(data.Cles, data.CategorieParCle, events.Presences, events.Absences, events.Retards));
    }

    private static DashboardAbsencesDto BuildAbsences(
        DashboardData data, DashboardEvents events, IReadOnlyList<(DateTime Cle, string Libelle)> periodes, string periode)
    {
        var absences = events.Absences;
        var parPeriode = absences
            .GroupBy(a => PeriodGrouping.Grouper(a.Date, periode).Cle)
            .ToDictionary(g => g.Key, g => g.Count());
        var stats = periodes
            .Select(p => new DashboardGroupDto(p.Libelle, parPeriode.GetValueOrDefault(p.Cle), 0))
            .ToList();
        var top = absences
            .GroupBy(a => a.Cle, StringComparer.OrdinalIgnoreCase)
            .Select(g => DashboardIndicators.ToTop(g.Key, g.Count(), 0, data.Cles))
            .OrderByDescending(t => t.Count)
            .Take(5)
            .ToList();
        var explicites = absences.Count(a => data.CorrectionsParCle.TryGetValue(a.Cle, out var l)
            && l.Any(h => h.DatePointageIn?.Date == a.Date && (h.AbsenceAm == true || h.AbsencePm == true)));
        return new DashboardAbsencesDto(absences.Count, explicites, absences.Count - explicites, stats, top);
    }

    private static DashboardRetardsDto BuildRetards(
        DashboardData data, DashboardEvents events, IReadOnlyList<(DateTime Cle, string Libelle)> periodes, string periode)
    {
        var retards = events.Retards;
        var parPeriode = retards
            .GroupBy(r => PeriodGrouping.Grouper(r.Date, periode).Cle)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Minutes: g.Sum(r => r.Minutes)));
        var stats = periodes
            .Select(p => parPeriode.TryGetValue(p.Cle, out var retard)
                ? new DashboardGroupDto(p.Libelle, retard.Count, retard.Minutes)
                : new DashboardGroupDto(p.Libelle, 0, 0))
            .ToList();
        var top = retards
            .GroupBy(r => r.Cle, StringComparer.OrdinalIgnoreCase)
            .Select(g => DashboardIndicators.ToTop(g.Key, g.Count(), g.Sum(r => r.Minutes), data.Cles))
            .OrderByDescending(t => t.TotalMinutes)
            .Take(5)
            .ToList();
        return new DashboardRetardsDto(retards.Count, events.MinutesRetard, stats, top);
    }

    private static List<DashboardSerieDto> BuildSeries(
        List<SemaineHs> semaines, IReadOnlyList<(DateTime Cle, string Libelle)> periodes, string periode)
    {
        var heuresParPeriode = semaines
            .GroupBy(s => PeriodGrouping.Grouper(s.DateDeb, periode).Cle)
            .ToDictionary(g => g.Key, g => (Ht: g.Sum(s => s.Ht), Hs: g.Sum(s => s.Hs)));
        return periodes
            .Select(p => heuresParPeriode.TryGetValue(p.Cle, out var heures)
                ? new DashboardSerieDto(p.Libelle, heures.Ht, heures.Hs)
                : new DashboardSerieDto(p.Libelle, 0m, 0m))
            .ToList();
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
            DashboardIndicators.Valeurs(cartes.Select(c => c.Departement)),
            categories);
    }

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

    private static readonly PeriodeMetrics VideMetrics = new(0, 0, 0, 0, 0, Array.Empty<SemaineHs>());}
