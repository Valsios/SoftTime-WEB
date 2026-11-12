using SoftTime.Application.Abstractions.Employees;
using SoftTime.Application.Abstractions.ExternalData;
using SoftTime.Application.Abstractions.Landing;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Infrastructure.Services.Employees;

namespace SoftTime.Infrastructure.Services.Landing;

/// <summary>
/// Journées de calcul HS de chaque salarié, construites comme l'écran de validation de semaine :
/// heures corrigées, planning de la carte et de la pointeuse actives, fériés, congés SAGE.
/// </summary>
public sealed class LandingContextLoader : ILandingContextLoader
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly ISageLeaveCalendar _leaves;
    private readonly IExternalSourceService _externalSource;

    public LandingContextLoader(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        ISageLeaveCalendar leaves,
        IExternalSourceService externalSource)
    {
        _uow = uow;
        _tenant = tenant;
        _leaves = leaves;
        _externalSource = externalSource;
    }

    public async Task<LandingContext> LoadAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var period = new LandingPeriod(from.Date, to.Date);
        var reference = await LoadReferenceAsync(period, ct);
        var corrections = await LoadCorrectionsAsync(period, ct);
        var plannings = await LoadPlanningsAsync(period, ct);
        var conges = await _leaves.LoadAsync(ct);

        var employees = (await LoadCardsAsync(ct))
            .Select(card =>
            {
                var cle = MatriculeIdentity.Cle(card.SageMatricule);
                var sources = new LandingEmployeeSources(
                    corrections.GetValueOrDefault(cle),
                    plannings.GetValueOrDefault(card.Id),
                    conges.GetValueOrDefault(cle));
                return new LandingEmployee(card, reference.Config(card), LandingCalendar.Jours(period, reference.Feries, sources));
            })
            .ToList();

        var pointages = corrections.Values.SelectMany(lignes => lignes).Select(h => h.DatePointageIn!.Value.Date).ToList();
        return new LandingContext(employees, pointages.Count == 0 ? null : pointages.Max());
    }

    /// <summary>Une carte par clé matricule (la plus ancienne), limitée à la pointeuse active.</summary>
    private async Task<List<CardPaie>> LoadCardsAsync(CancellationToken ct)
    {
        var (sageDb, pointeuseDb) = (_tenant.SageDb, _tenant.PointeuseDb);
        return (await _uow.Repository<CardPaie>().ListReadOnlyAsync(c => c.SageBdd == sageDb, ct))
            .Where(c => !string.IsNullOrWhiteSpace(c.SageMatricule))
            .Where(c => string.IsNullOrWhiteSpace(c.PointeuseBdd)
                        || string.Equals(c.PointeuseBdd.Trim(), pointeuseDb.Trim(), StringComparison.OrdinalIgnoreCase))
            .GroupBy(c => MatriculeIdentity.Cle(c.SageMatricule))
            .Select(g => g.OrderBy(c => c.Id).First())
            .ToList();
    }

    private async Task<LandingReference> LoadReferenceAsync(LandingPeriod period, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        var feries = (await _uow.Repository<JourFerie>().ListReadOnlyAsync(f => f.BddSage == sageDb, ct))
            .Where(f => f.Date.HasValue)
            .Select(f => f.Date!.Value.Date)
            .ToHashSet();
        var civilites = OvertimeDayRules.ContientHuitMars(period.From, period.To)
            ? await SageCivilites.ChargerAsync(_tenant, _externalSource, ct)
            : new Dictionary<string, byte?>(StringComparer.Ordinal);

        return new LandingReference
        {
            MultiPointage = (await _uow.Repository<ParametrePointeuse>().ListReadOnlyAsync(_ => true, ct)).FirstOrDefault()?.MultiPoint ?? true,
            Categories = await _uow.Repository<Categorie>().ListReadOnlyAsync(c => c.BddSage == sageDb, ct),
            Affectations = await _uow.Repository<CategorieSalarie>().ListReadOnlyAsync(a => a.BddSage == sageDb, ct),
            Tolerances = await _uow.Repository<ToleranceRetard>().ListReadOnlyAsync(_ => true, ct),
            Majorations = await _uow.Repository<Majoration>().ListReadOnlyAsync(_ => true, ct),
            Feries = feries,
            Civilites = civilites
        };
    }

    private async Task<Dictionary<string, List<HeureCorrigee>>> LoadCorrectionsAsync(LandingPeriod period, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        var (from, apres) = (period.From, period.To.AddDays(1));
        return (await _uow.Repository<HeureCorrigee>().ListReadOnlyAsync(
                h => h.BddSage == sageDb
                     && h.MatriculeSage != null
                     && h.DatePointageIn >= from
                     && h.DatePointageIn < apres, ct))
            .GroupBy(h => MatriculeIdentity.Cle(h.MatriculeSage))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
    }

    /// <summary>Planning de l'écran HS : celui de la carte, pour la pointeuse active (ou sans pointeuse).</summary>
    private async Task<Dictionary<int, List<PlanningSalarie>>> LoadPlanningsAsync(LandingPeriod period, CancellationToken ct)
    {
        var (sageDb, pointeuseDb) = (_tenant.SageDb, _tenant.PointeuseDb);
        var (from, to) = (period.From, period.To);
        return (await _uow.Repository<PlanningSalarie>().ListReadOnlyAsync(
                p => p.BddSage == sageDb && p.DateP >= from && p.DateP <= to, ct))
            .Where(p => p.DateP.HasValue && OvertimeDayRules.EstDeLaPointeuse(p, pointeuseDb))
            .GroupBy(p => p.IdSal)
            .ToDictionary(g => g.Key, g => g.ToList());
    }
}
