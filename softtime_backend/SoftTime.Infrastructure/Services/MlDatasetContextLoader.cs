using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public sealed class MlDatasetContextLoader : IMlDatasetContextLoader
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly ISageLeaveCalendar _leaves;
    private readonly IExternalSourceService _externalSource;
    private readonly MlOptions _options;

    public MlDatasetContextLoader(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        ISageLeaveCalendar leaves,
        IExternalSourceService externalSource,
        IOptions<MlOptions> options)
    {
        _uow = uow;
        _tenant = tenant;
        _leaves = leaves;
        _externalSource = externalSource;
        _options = options.Value;
    }

    // ------------------------------------------------------------------ contexte

    public async Task<DatasetContext> LoadContextAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var pointeuseDb = _tenant.PointeuseDb;
        var warmupFrom = from.AddDays(-Math.Max(0, _options.WarmupDays));

        var allCards = (await _uow.Repository<CardPaie>().ListAsync(c => c.SageBdd == sageDb, ct))
            .Where(c => !string.IsNullOrWhiteSpace(c.SageMatricule))
            .ToList();
        var cards = allCards
            .Where(c => string.IsNullOrWhiteSpace(c.PointeuseBdd)
                        || string.Equals(c.PointeuseBdd.Trim(), pointeuseDb.Trim(), StringComparison.OrdinalIgnoreCase))
            .GroupBy(c => MlDatasetProjection.MlKey(c.SageMatricule))
            .Select(g => g.OrderBy(c => c.Id).First())
            .ToList();

        var categories = await _uow.Repository<Categorie>().ListAsync(c => c.BddSage == sageDb, ct);
        var affectations = await _uow.Repository<CategorieSalarie>().ListAsync(a => a.BddSage == sageDb, ct);
        var tolerances = await _uow.Repository<ToleranceRetard>().ListAsync(_ => true, ct);
        var majorations = await _uow.Repository<Majoration>().ListAsync(_ => true, ct);
        var feries = (await _uow.Repository<JourFerie>().ListAsync(f => f.BddSage == sageDb, ct))
            .Where(f => f.Date.HasValue)
            .Select(f => f.Date!.Value.Date)
            .ToHashSet();
        var clock = (await _uow.Repository<ParametrePointeuse>().ListAsync(_ => true, ct)).FirstOrDefault();
        var multiPointage = clock?.MultiPoint ?? true;

        var corrections = (await _uow.Repository<HeureCorrigee>().ListAsync(
                h => h.BddSage == sageDb
                     && h.MatriculeSage != null
                     && h.DatePointageIn >= warmupFrom
                     && h.DatePointageIn <= to, ct))
            .GroupBy(h => MlDatasetProjection.MlKey(h.MatriculeSage))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var plannings = (await _uow.Repository<PlanningSalarie>().ListAsync(
                p => p.BddSage == sageDb && p.DateP >= warmupFrom && p.DateP <= to, ct))
            .Where(p => p.DateP.HasValue)
            .ToList();

        // Vue HS (écran heures supplémentaires) : planning de la carte et de la pointeuse active.
        var planningHs = plannings
            .Where(p => OvertimeDayRules.EstDeLaPointeuse(p, pointeuseDb))
            .GroupBy(p => p.IdSal)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Vue absences / retards (états et tableau de bord) : plannings de toutes les cartes de même
        // clé matricule (C11), toutes pointeuses confondues.
        var cleParIdSal = allCards
            .GroupBy(c => c.Id)
            .ToDictionary(g => g.Key, g => MlDatasetProjection.MlKey(g.First().SageMatricule));
        var planningEtats = plannings
            .Where(p => cleParIdSal.ContainsKey(p.IdSal))
            .GroupBy(p => cleParIdSal[p.IdSal], StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var categorieParCarte = affectations
            .GroupBy(a => a.MatriculeSage)
            .ToDictionary(g => g.Key, g => g.First().IdCateg);

        var conges = await _leaves.LoadAsync(ct);
        var civilites = OvertimeDayRules.ContientHuitMars(warmupFrom, to)
            ? await SageCivilites.ChargerAsync(_tenant, _externalSource, ct)
            : new Dictionary<string, byte?>(StringComparer.Ordinal);

        var validees = (await _uow.Repository<HsValidee>().ListAsync(
                v => v.BddSage == sageDb
                     && v.ValidationHs == true
                     && v.DateDeb != null
                     && v.DateDeb >= from.AddDays(-7)
                     && v.DateDeb <= to, ct))
            .Where(v => v.DateDeb.HasValue && !string.IsNullOrWhiteSpace(v.Matricules))
            .GroupBy(v => (MlDatasetProjection.MlKey(v.Matricules), v.DateDeb!.Value.Date))
            .ToDictionary(g => g.Key, g => g.OrderBy(v => v.Id).First());

        var ctx = new DatasetContext
        {
            SageDb = sageDb,
            PointeuseDb = pointeuseDb,
            From = from,
            To = to,
            WarmupFrom = warmupFrom,
            ObservedUntil = DateTime.Today,
            MultiPointage = multiPointage,
            Cards = cards,
            Categories = categories,
            Affectations = affectations,
            Tolerances = tolerances,
            Majorations = majorations,
            Feries = feries,
            Corrections = corrections,
            PlanningHs = planningHs,
            PlanningEtats = planningEtats,
            CategorieParCarte = categorieParCarte,
            Conges = conges,
            Civilites = civilites,
            HsValidees = validees
        };

        foreach (var card in cards)
        {
            var cle = MlDatasetProjection.MlKey(card.SageMatricule);
            ctx.CardByKey[cle] = card;
            ctx.Configs[cle] = MlDayFeatures.BuildConfig(ctx, card);
            ctx.EmployeeDays[cle] = new List<DayRow>();
        }

        return ctx;
    }
}
