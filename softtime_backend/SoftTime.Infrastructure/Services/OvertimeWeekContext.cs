using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public sealed class OvertimeWeekContext : IOvertimeWeekContext
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IExternalSourceService _externalSource;
    private readonly ISageLeaveCalendar _leaves;

    public OvertimeWeekContext(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IExternalSourceService externalSource,
        ISageLeaveCalendar leaves)
    {
        _uow = uow;
        _tenant = tenant;
        _externalSource = externalSource;
        _leaves = leaves;
    }

    // ------------------------------------------------------------------ contexte du calcul

    public async Task<WeeklyContext> ChargerContexteAsync(PeriodRequest request, bool avecExistantes, CancellationToken ct)
    {
        var from = request.From.Date;
        var to = request.To.Date;
        var sageDb = _tenant.SageDb;
        var pointeuseDb = _tenant.PointeuseDb;

        var cards = (await TargetCardsAsync(request, ct))
            .OrderBy(c => c.SageMatricule, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var clock = (await _uow.Repository<ParametrePointeuse>().ListAsync(_ => true, ct)).FirstOrDefault();

        var heures = (await _uow.Repository<HeureCorrigee>().ListAsync(
                h => h.BddSage == sageDb
                     && h.MatriculeSage != null
                     && h.DatePointageIn >= from
                     && h.DatePointageIn <= to, ct))
            .GroupBy(h => MatriculeIdentity.Cle(h.MatriculeSage))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var planning = (await _uow.Repository<PlanningSalarie>().ListAsync(
                p => p.BddSage == sageDb && p.DateP >= from && p.DateP <= to, ct))
            .Where(p => OvertimeDayRules.EstDeLaPointeuse(p, pointeuseDb))
            .GroupBy(p => p.IdSal)
            .ToDictionary(g => g.Key, g => g.ToList());

        var existantes = new Dictionary<string, HsValidee>(StringComparer.Ordinal);
        if (avecExistantes)
        {
            existantes = (await _uow.Repository<HsValidee>().ListAsync(
                    x => x.BddSage == sageDb && x.DateDeb == from, ct))
                .Where(x => !string.IsNullOrEmpty(x.Matricules))
                .GroupBy(x => MatriculeIdentity.Cle(x.Matricules))
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Id).First(), StringComparer.Ordinal);
        }

        return new WeeklyContext
        {
            SageDb = sageDb,
            PointeuseDb = pointeuseDb,
            MultiPointage = clock?.MultiPoint ?? true,
            Cards = cards,
            Feries = await _uow.Repository<JourFerie>().ListAsync(f => f.BddSage == sageDb, ct),
            Categories = await _uow.Repository<Categorie>().ListAsync(c => c.BddSage == sageDb, ct),
            Affectations = await _uow.Repository<CategorieSalarie>().ListAsync(a => a.BddSage == sageDb, ct),
            Tolerances = await _uow.Repository<ToleranceRetard>().ListAsync(_ => true, ct),
            Majorations = await _uow.Repository<Majoration>().ListAsync(_ => true, ct),
            Conges = await _leaves.LoadAsync(ct),
            Heures = heures,
            Planning = planning,
            Existantes = existantes,
            Civilites = OvertimeDayRules.ContientHuitMars(from, to)
                ? await ChargerCivilitesAsync(ct)
                : new Dictionary<string, byte?>(StringComparer.Ordinal)
        };
    }

    /// <summary>
    /// Paramétrage du salarié : catégorie (heures hebdo, pause), tolérances, priorité des majorations
    /// et civilité (règle du 8 mars). Les valeurs absentes retombent sur les défauts de la synthèse.
    /// </summary>
    public OvertimeWeekConfig Configurer(WeeklyContext ctx, CardPaie card)
    {
        var affectation = ctx.Affectations.FirstOrDefault(a => a.MatriculeSage == card.Id);
        var categorie = affectation == null
            ? null
            : ctx.Categories.FirstOrDefault(c => c.IdCategorie == affectation.IdCateg);
        var tolerance = categorie == null
            ? null
            : ctx.Tolerances.FirstOrDefault(t => t.IdCategorie == categorie.IdCategorie && !t.TypesTol);
        ctx.Civilites.TryGetValue(MatriculeIdentity.Cle(card.SageMatricule), out var civilite);

        return new OvertimeWeekConfig(
            MultiPointage: ctx.MultiPointage,
            HeuresSemaine: categorie?.HeureSemaine ?? OvertimeRules.DefaultWeeklyHours,
            PauseCategorie: categorie?.Pause,
            ToleranceEntreeMinutes: tolerance?.Tolerance ?? 0,
            PasSortieMinutes: tolerance?.Sortie,
            CotationNuit: Cotation(ctx.Majorations, "NUIT"),
            CotationDimanche: Cotation(ctx.Majorations, "DIMANCHE"),
            CotationFerie: Cotation(ctx.Majorations, "FERIE"),
            EstFemme: OvertimeDayRules.EstFemme(civilite));
    }

    private static decimal Cotation(IReadOnlyList<Majoration> majorations, string type)
        => majorations
            .Where(m => string.Equals(m.Mojoration?.Trim(), type, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Cotation ?? 0m)
            .DefaultIfEmpty(0m)
            .Max();

    /// <summary>Journées du calcul : paires, planning (reconstruit s'il manque), férié, congé, retard saisi.</summary>
    public List<OvertimeDayInput> ConstruireJours(WeeklyContext ctx, CardPaie card, DateTime from, DateTime to)
    {
        var cle = MatriculeIdentity.Cle(card.SageMatricule);
        var heures = ctx.Heures.GetValueOrDefault(cle) ?? new List<HeureCorrigee>();
        var planning = ctx.Planning.GetValueOrDefault(card.Id) ?? new List<PlanningSalarie>();
        var conges = ctx.Conges.GetValueOrDefault(cle) ?? new List<LeaveDay>();
        var jours = new List<OvertimeDayInput>();

        // Construction partagée avec la base analytique du moteur prédictif.
        for (var jour = from.Date; jour <= to.Date; jour = jour.AddDays(1))
        {
            jours.Add(OvertimeDayRules.Construire(
                jour,
                heures.Where(h => h.DatePointageIn.HasValue && h.DatePointageIn.Value.Date == jour),
                OvertimeDayRules.PlanDuJour(planning.Where(p => p.DateP.HasValue && p.DateP.Value.Date == jour)),
                ctx.Feries.Any(f => f.Date.HasValue && f.Date.Value.Date == jour),
                LeaveRules.Valeur(conges, jour)));
        }
        return jours;
    }

    /// <summary>La civilité n'est chargée que si la période contient un 8 mars (évite un appel SAGE systématique).</summary>
    private Task<Dictionary<string, byte?>> ChargerCivilitesAsync(CancellationToken ct)
        => SageCivilites.ChargerAsync(_tenant, _externalSource, ct);

    /// <summary>
    /// Bornes des lundis (<c>DATE_DEB</c>) des semaines rattachées à une période EXO/IMPO : une semaine
    /// appartient à la période qui contient son dimanche, ce qui l'affecte à une seule période.
    /// </summary>
    public (DateTime DebutMin, DateTime DebutMax) BornesSemainesDeLaPeriode(DateTime from, DateTime to)
        => (from.Date.AddDays(-6), to.Date.AddDays(-6));

    public void EnsureMaxOneMonth(PeriodRequest request)
    {
        if (request.To.Date < request.From.Date)
            throw new DomainException("La date de début doit être antérieure ou égale à la date de fin.");
        if (!OvertimeRules.IsMaxOneMonth(request.From, request.To))
            throw new DomainException("La période sélectionnée ne doit pas dépasser un mois.");
    }

    /// <summary>
    /// Cartes du périmètre. Le filtre pointeuse est tolérant (lignes héritées sans base renseignée)
    /// et identique à celui du pré-traitement des pointages.
    /// </summary>
    public async Task<List<CardPaie>> TargetCardsAsync(PeriodRequest request, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        var pointeuseDb = _tenant.PointeuseDb;
        var cards = await _uow.Repository<CardPaie>().ListAsync(c =>
            c.SageBdd == sageDb
            && (string.IsNullOrEmpty(request.Departement)
                || string.Equals(c.Departement, request.Departement, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrEmpty(request.MatriculeFrom) || string.Compare(c.SageMatricule, request.MatriculeFrom) >= 0)
            && (string.IsNullOrEmpty(request.MatriculeTo) || string.Compare(c.SageMatricule, request.MatriculeTo) <= 0), ct);
        return cards
            .Where(c => string.IsNullOrWhiteSpace(c.PointeuseBdd)
                        || string.Equals(c.PointeuseBdd.Trim(), pointeuseDb.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(c => !string.IsNullOrWhiteSpace(c.SageMatricule))
            .GroupBy(c => MatriculeIdentity.Cle(c.SageMatricule))
            .Select(g => g.OrderBy(c => c.Id).First())
            .ToList();
    }
}
