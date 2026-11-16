using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions.Closing;
using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Closing;

/// <summary>
/// Checklist d'avancement de la paie d'une période (lecture seule) : pointages importés, anomalies à arbitrer, semaines
/// validées, calcul EXO/IMPO, heures imposables et synchronisation paie. Séparée de l'analyse : elle ne crée aucune
/// anomalie et n'entre ni dans le score ni dans le statut (voir <see cref="ClosingChecklistLines"/>).
/// </summary>
public sealed class ClosingChecklistService : IClosingChecklistService
{
    /// <summary>Durée de réutilisation de la checklist (60 s, ou moins si le délai glissant est plus court ; 0 : aucun
    /// cache).</summary>
    private const int DureeCacheSecondes = 60;

    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IClosingChecklistCache _cache;
    private readonly ClosingOptions _reglages;
    private readonly TimeProvider _horloge;
    private readonly ILogger<ClosingChecklistService> _logger;
    private readonly IPayrollVariableService? _variables;

    public ClosingChecklistService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IClosingChecklistCache cache,
        IOptions<ClosingOptions> reglages,
        TimeProvider horloge,
        ILogger<ClosingChecklistService> logger,
        IPayrollVariableService? variables = null)
    {
        _uow = uow;
        _tenant = tenant;
        _cache = cache;
        _reglages = reglages.Value;
        _horloge = horloge;
        _logger = logger;
        _variables = variables;
    }

    /// <summary>
    /// Checklist de la période (mois civil précédent sans dates) pour le département demandé. Le périmètre de la
    /// demande entre dans la clé du cache : deux utilisateurs ne partagent jamais une entrée. <see
    /// cref="ClosingChecklistRequest.ReuseCached"/>
    /// autorise la réutilisation de la dernière checklist pendant 60 s au plus.
    /// </summary>
    public async Task<ClosingChecklistDto> ChecklistAsync(
        ClosingChecklistRequest request, CancellationToken ct = default)
    {
        request = AvecPeriode(request);
        await _tenant.EnsureAuthorizedAsync(ct);

        var cle = ClosingChecklistCache.Cle(request, _tenant.SageDb, _tenant.PointeuseDb);
        var maintenant = _horloge.GetUtcNow();
        if (_reglages.CacheSeconds > 0 && request.ReuseCached && _cache.TryGet(cle, maintenant, out var conservee))
            return conservee;

        var resultat = await ConstruireAsync(request, ct);
        if (_reglages.CacheSeconds > 0)
            _cache.Set(cle, resultat, maintenant,
                TimeSpan.FromSeconds(Math.Min(DureeCacheSecondes, _reglages.CacheSeconds)));
        _logger.LogInformation("Checklist de clôture {From:yyyy-MM-dd} → {To:yyyy-MM-dd} : {Lignes} ligne(s).",
            resultat.From, resultat.To, resultat.Lignes.Count);
        return resultat;
    }

    /// <summary>Période par défaut et contrôles de l'analyse (bornes, longueur maximale).</summary>
    private ClosingChecklistRequest AvecPeriode(ClosingChecklistRequest request)
    {
        var periode = ClosingComputation.AvecPeriodeParDefaut(
            new ClosingAnalysisRequest(request.From, request.To, request.Departement), _horloge.GetLocalNow().Date);
        ClosingComputation.Valider(periode, _reglages.MaxPeriodDays);
        return request with { From = periode.From.Date, To = periode.To.Date };
    }

    private async Task<ClosingChecklistDto> ConstruireAsync(ClosingChecklistRequest request, CancellationToken ct)
    {
        var cartes = await ClosingDataQueries.CartesAsync(_uow, _tenant, request.Departement, ct);
        var scope = new ClosingLoadScope(
            _tenant.SageDb, _tenant.PointeuseDb, request.From, request.To,
            cartes.Select(c => c.Id).ToHashSet(),
            cartes.Select(c => MatriculeIdentity.Cle(c.SageMatricule)).ToHashSet(StringComparer.Ordinal));
        var donnees = await LireAsync(scope, cartes, ct);
        var departement = string.IsNullOrWhiteSpace(request.Departement) ? null : request.Departement.Trim();
        return new ClosingChecklistDto(
            request.From, request.To, departement, ClosingChecklistLines.Construire(donnees));
    }

    private async Task<ClosingChecklistData> LireAsync(
        ClosingLoadScope scope, List<CardPaie> cartes, CancellationToken ct)
    {
        var cleParIdSal = cartes.ToDictionary(c => c.Id, c => MatriculeIdentity.Cle(c.SageMatricule));
        var (pointages, salaries) = await ClosingChecklistQueries.PointagesAsync(_uow, scope, ct);
        var anomalies = await ClosingChecklistQueries.AnomaliesAsync(_uow, scope, ct);
        var corrections = await ClosingChecklistQueries.CorrectionsAsync(_uow, scope, ct);
        var (attendues, validees) = await ClosingChecklistQueries.SemainesAsync(_uow, scope, cleParIdSal, ct);
        var recoupantes = await ClosingChecklistQueries.ExoImpoRecoupantesAsync(_uow, scope, ct);
        var exactes = recoupantes
            .Where(h => h.PeriodeDebut?.Date == scope.Debut && h.PeriodeFin?.Date == scope.Fin)
            .ToList();
        return new ClosingChecklistData(
            pointages, salaries, anomalies, corrections, attendues, validees,
            exactes.Count,
            recoupantes.Count,
            recoupantes.Select(h => (h.PeriodeDebut, h.PeriodeFin)).Distinct().Count(),
            await SalariesImposablesAsync(exactes, ct),
            await ClosingChecklistQueries.DernierResumeSynchroAsync(_uow, scope, ct));
    }

    /// <summary>
    /// Salariés avec des heures imposables envoyées à la paie. Null si IMPO 130 et IMPO 150 sont décochées pour la
    /// base : la ligne n'a alors pas d'objet. Avec la configuration par défaut, comptage d'origine (I130 + I150).
    /// </summary>
    private async Task<int?> SalariesImposablesAsync(List<HsExoImp> exactes, CancellationToken ct)
    {
        var selection = _variables is null ? PayrollVariableSelection.Default : await _variables.GetSelectionAsync(_tenant.SageDb, ct);
        var i130 = selection.EstActive(PayrollVariableSelection.I130);
        var i150 = selection.EstActive(PayrollVariableSelection.I150);
        if (!i130 && !i150)
            return null;
        return exactes.Count(h => (i130 ? h.I130 ?? 0m : 0m) + (i150 ? h.I150 ?? 0m : 0m) > 0m);
    }
}
