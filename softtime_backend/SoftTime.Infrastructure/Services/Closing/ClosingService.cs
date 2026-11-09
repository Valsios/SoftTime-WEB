using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions.Closing;
using SoftTime.Application.Abstractions.Employees;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Closing;

public sealed class ClosingService : IClosingService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly ISageLeaveCalendar _leaves;
    private readonly TimeProvider _horloge;
    private readonly ClosingOptions _reglages;
    private readonly ClosingAnalysisCache _cache;

    public ClosingService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        ISageLeaveCalendar leaves,
        TimeProvider? horloge = null,
        IOptions<ClosingOptions>? reglages = null,
        ClosingAnalysisCache? cache = null)
    {
        _uow = uow;
        _tenant = tenant;
        _leaves = leaves;
        _horloge = horloge ?? TimeProvider.System;
        _reglages = reglages?.Value ?? new ClosingOptions();
        _cache = cache ?? new ClosingAnalysisCache();
    }

    /// <summary>
    /// Analyse d'une période. Sans options de règles dans la demande, les réglages « Closing »
    /// s'appliquent (tolérance, semaines validées, gravités, poids, seuil de statut).
    /// </summary>
    public async Task<ClosingAnalysisDto> AnalyserAsync(ClosingAnalysisRequest request, CancellationToken ct = default)
    {
        request = ClosingComputation.AvecPeriodeParDefaut(request, Today());
        request = request with { Options = request.Options ?? _reglages.ReglesParDefaut() };
        ClosingComputation.Valider(request, _reglages.MaxPeriodDays);

        if (_reglages.CacheSeconds <= 0)
            return ClosingComputation.Calculer(await ChargerAsync(request, ct), request);

        // Droit d'accès à la base vérifié AVANT toute lecture du cache (le contrôleur a déjà vérifié le droit de lecture).
        await _tenant.EnsureAuthorizedAsync(ct);
        var cle = ClosingAnalysisCache.Cle(_tenant.SageDb, _tenant.PointeuseDb, request);
        var maintenant = _horloge.GetUtcNow();
        var fenetre = ClosingCacheWindow.Depuis(_reglages.CacheSeconds, _reglages.CacheMaxSeconds);
        if (request.ReuseCached && _cache.TryGet(cle, maintenant, fenetre.Glissant, out var preparee))
            return ClosingComputation.Assembler(preparee, request);

        // Sans réutilisation (ou entrée expirée) : calcul à neuf, qui remplace l'entrée avec un plafond neuf.
        preparee = ClosingComputation.Preparer(await ChargerAsync(request, ct), request);
        _cache.Set(cle, preparee, maintenant, fenetre);
        return ClosingComputation.Assembler(preparee, request);
    }

    /// <summary>
    /// Chargement ensembliste de la période demandée (et de la période précédente seulement si la
    /// comparaison est demandée). Le filtre département s'applique aux cartes, puis toutes les autres
    /// données sont restreintes aux salariés retenus avant le calcul (voir <see cref="ClosingDataQueries"/>).
    /// </summary>
    internal async Task<ClosingData> ChargerAsync(ClosingAnalysisRequest request, CancellationToken ct)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var (debutPrecedent, _) = ClosingComputation.PeriodePrecedente(request.From, request.To);
        var debut = request.IncludeComparison ? debutPrecedent : request.From.Date;

        var clock = (await _uow.Repository<ParametrePointeuse>().ListAsync(_ => true, ct)).FirstOrDefault()
            ?? throw new DomainException("Paramètre pointeuse (T_CLOCK) manquant.");
        var cartes = await ClosingDataQueries.CartesAsync(_uow, _tenant, request.Departement, ct);
        var feries = await ChargerFeriesAsync(ct);
        if (cartes.Count == 0)
            return new ClosingData(clock.MultiPoint == true, cartes,
                new List<PlanningSalarie>(), new List<Pointage>(), new List<HeureCorrigee>(),
                new List<HsValidee>(), feries, new Dictionary<string, List<LeaveDay>>(), Today());

        var scope = new ClosingLoadScope(
            _tenant.SageDb, _tenant.PointeuseDb, debut, request.To.Date,
            cartes.Select(c => c.Id).ToHashSet(),
            cartes.Select(c => MatriculeIdentity.Cle(c.SageMatricule)).ToHashSet(StringComparer.Ordinal));
        return new ClosingData(clock.MultiPoint == true, cartes,
            await ClosingDataQueries.PlanningsAsync(_uow, scope, ct),
            await ClosingDataQueries.PointagesAsync(_uow, scope, ct),
            await ClosingDataQueries.CorrectionsAsync(_uow, scope, ct),
            await ClosingDataQueries.SemainesValideesAsync(_uow, scope, ct),
            feries,
            await ChargerCongesAsync(scope.Cles, ct),
            Today());
    }

    private async Task<HashSet<DateTime>> ChargerFeriesAsync(CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        return (await _uow.Repository<JourFerie>().ListAsync(f => f.BddSage == sageDb, ct))
            .Where(f => f.Date.HasValue)
            .Select(f => f.Date!.Value.Date)
            .ToHashSet();
    }

    private async Task<Dictionary<string, List<LeaveDay>>> ChargerCongesAsync(HashSet<string> cles, CancellationToken ct) =>
        (await _leaves.LoadAsync(ct))
            .Where(kv => cles.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

    private DateTime Today() => _horloge.GetLocalNow().Date;
}
