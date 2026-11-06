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

    public async Task<DatasetContext> LoadContextAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var pointeuseDb = _tenant.PointeuseDb;
        var warmupFrom = from.AddDays(-Math.Max(0, _options.WarmupDays));
        var allCards = (await _uow.Repository<CardPaie>().ListAsync(c => c.SageBdd == sageDb, ct))
            .Where(c => !string.IsNullOrWhiteSpace(c.SageMatricule))
            .ToList();
        var affectations = await _uow.Repository<CategorieSalarie>().ListAsync(a => a.BddSage == sageDb, ct);
        var plannings = await LoadPlanningsAsync(warmupFrom, to, ct);

        var ctx = new DatasetContext
        {
            SageDb = sageDb,
            PointeuseDb = pointeuseDb,
            From = from,
            To = to,
            WarmupFrom = warmupFrom,
            ObservedUntil = DateTime.Today,
            MultiPointage = (await _uow.Repository<ParametrePointeuse>().ListAsync(_ => true, ct)).FirstOrDefault()?.MultiPoint ?? true,
            Cards = ActiveCards(allCards, pointeuseDb),
            Categories = await _uow.Repository<Categorie>().ListAsync(c => c.BddSage == sageDb, ct),
            Affectations = affectations,
            Tolerances = await _uow.Repository<ToleranceRetard>().ListAsync(_ => true, ct),
            Majorations = await _uow.Repository<Majoration>().ListAsync(_ => true, ct),
            Feries = await LoadFeriesAsync(ct),
            Corrections = await LoadCorrectionsAsync(warmupFrom, to, ct),
            PlanningHs = PlanningHsParCarte(plannings, pointeuseDb),
            PlanningEtats = PlanningEtatsParCle(plannings, allCards),
            CategorieParCarte = affectations.GroupBy(a => a.MatriculeSage).ToDictionary(g => g.Key, g => g.First().IdCateg),
            Conges = await _leaves.LoadAsync(ct),
            Civilites = await LoadCivilitesAsync(warmupFrom, to, ct),
            HsValidees = await LoadValideesAsync(from, to, ct)
        };
        IndexCards(ctx);
        return ctx;
    }

    /// <summary>Une carte par clé matricule (la plus ancienne), limitée à la pointeuse active.</summary>
    private static List<CardPaie> ActiveCards(List<CardPaie> allCards, string pointeuseDb) =>
        allCards
            .Where(c => string.IsNullOrWhiteSpace(c.PointeuseBdd)
                        || string.Equals(c.PointeuseBdd.Trim(), pointeuseDb.Trim(), StringComparison.OrdinalIgnoreCase))
            .GroupBy(c => MlDatasetProjection.MlKey(c.SageMatricule))
            .Select(g => g.OrderBy(c => c.Id).First())
            .ToList();

    private async Task<HashSet<DateTime>> LoadFeriesAsync(CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        return (await _uow.Repository<JourFerie>().ListAsync(f => f.BddSage == sageDb, ct))
            .Where(f => f.Date.HasValue)
            .Select(f => f.Date!.Value.Date)
            .ToHashSet();
    }

    private async Task<Dictionary<string, List<HeureCorrigee>>> LoadCorrectionsAsync(
        DateTime warmupFrom, DateTime to, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        return (await _uow.Repository<HeureCorrigee>().ListAsync(
                h => h.BddSage == sageDb
                     && h.MatriculeSage != null
                     && h.DatePointageIn >= warmupFrom
                     && h.DatePointageIn <= to, ct))
            .GroupBy(h => MlDatasetProjection.MlKey(h.MatriculeSage))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
    }

    private async Task<List<PlanningSalarie>> LoadPlanningsAsync(DateTime warmupFrom, DateTime to, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        return (await _uow.Repository<PlanningSalarie>().ListAsync(
                p => p.BddSage == sageDb && p.DateP >= warmupFrom && p.DateP <= to, ct))
            .Where(p => p.DateP.HasValue)
            .ToList();
    }

    /// <summary>Vue HS (écran heures supplémentaires) : planning de la carte et de la pointeuse active.</summary>
    private static Dictionary<int, List<PlanningSalarie>> PlanningHsParCarte(
        List<PlanningSalarie> plannings, string pointeuseDb) =>
        plannings
            .Where(p => OvertimeDayRules.EstDeLaPointeuse(p, pointeuseDb))
            .GroupBy(p => p.IdSal)
            .ToDictionary(g => g.Key, g => g.ToList());

    /// <summary>
    /// Vue absences / retards (états et tableau de bord) : plannings de toutes les cartes de même
    /// clé matricule (C11), toutes pointeuses confondues.
    /// </summary>
    private static Dictionary<string, List<PlanningSalarie>> PlanningEtatsParCle(
        List<PlanningSalarie> plannings, List<CardPaie> allCards)
    {
        var cleParIdSal = allCards
            .GroupBy(c => c.Id)
            .ToDictionary(g => g.Key, g => MlDatasetProjection.MlKey(g.First().SageMatricule));
        return plannings
            .Where(p => cleParIdSal.ContainsKey(p.IdSal))
            .GroupBy(p => cleParIdSal[p.IdSal], StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, byte?>> LoadCivilitesAsync(DateTime warmupFrom, DateTime to, CancellationToken ct) =>
        OvertimeDayRules.ContientHuitMars(warmupFrom, to)
            ? await SageCivilites.ChargerAsync(_tenant, _externalSource, ct)
            : new Dictionary<string, byte?>(StringComparer.Ordinal);

    private async Task<Dictionary<(string, DateTime), HsValidee>> LoadValideesAsync(
        DateTime from, DateTime to, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        return (await _uow.Repository<HsValidee>().ListAsync(
                v => v.BddSage == sageDb
                     && v.ValidationHs == true
                     && v.DateDeb != null
                     && v.DateDeb >= from.AddDays(-7)
                     && v.DateDeb <= to, ct))
            .Where(v => v.DateDeb.HasValue && !string.IsNullOrWhiteSpace(v.Matricules))
            .GroupBy(v => (MlDatasetProjection.MlKey(v.Matricules), v.DateDeb!.Value.Date))
            .ToDictionary(g => g.Key, g => g.OrderBy(v => v.Id).First());
    }

    private static void IndexCards(DatasetContext ctx)
    {
        foreach (var card in ctx.Cards)
        {
            var cle = MlDatasetProjection.MlKey(card.SageMatricule);
            ctx.CardByKey[cle] = card;
            ctx.Configs[cle] = MlDayFeatures.BuildConfig(ctx, card);
            ctx.EmployeeDays[cle] = new List<DayRow>();
        }
    }
}
