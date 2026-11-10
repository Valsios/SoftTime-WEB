using Microsoft.Extensions.Logging;

using SoftTime.Application.Abstractions.Employees;
using SoftTime.Application.Abstractions.Reports;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Infrastructure.Queries;

namespace SoftTime.Infrastructure.Services.Reports;

public sealed class ReportContextLoader : IReportContextLoader
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly ISageLeaveCalendar _leaves;
    private readonly ILogger<ReportContextLoader> _logger;

    public ReportContextLoader(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        ISageLeaveCalendar leaves,
        ILogger<ReportContextLoader> logger)
    {
        _uow = uow;
        _tenant = tenant;
        _leaves = leaves;
        _logger = logger;
    }

    /// <summary>
    /// Contexte partagé par l'état des absences et l'état des retards : cartes filtrées (base SAGE,
    /// plage de matricules, département), fériés, congés SAGE, corrections, plannings, affectations de
    /// catégorie et tolérances. La fenêtre est plafonnée à la veille : aucun état ne fabrique
    /// d'événement futur, exactement comme le tableau de bord.
    /// </summary>
    public async Task<ReportContext?> ChargerContexteAsync(ReportFilter filter, CancellationToken ct)
    {
        var from = filter.From.Date;
        var hier = DateTime.Today.AddDays(-1);
        var fin = filter.To.Date < hier ? filter.To.Date : hier;
        if (from > fin)
            return null;

        var cartes = await ChargerCartesAsync(filter, ct);
        if (cartes.Count == 0)
            return null;

        var sageDb = _tenant.SageDb;
        var feries = (await _uow.Repository<JourFerie>().ListAsync(f => f.BddSage == sageDb, ct))
            .Where(f => f.Date.HasValue)
            .Select(f => f.Date!.Value.Date)
            .ToHashSet();
        var conges = await ChargerCongesAsync(ct);
        var corrections = await ChargerCorrectionsAsync(cartes, from, fin.AddDays(1), ct);
        var plannings = await ChargerPlanningsAsync(cartes, from, fin.AddDays(1), ct);

        // Catégorie du salarié (T_CAT_SAL référence T_CARDPAIE.ID) et tolérances de catégorie
        var categorieParCarte = (await _uow.Repository<CategorieSalarie>().ListAsync(a => a.BddSage == sageDb, ct))
            .GroupBy(a => a.MatriculeSage)
            .ToDictionary(g => g.Key, g => g.First().IdCateg);
        var tolerances = await _uow.Repository<ToleranceRetard>().ListAsync(_ => true, ct);

        return new ReportContext(cartes, feries, conges, corrections, plannings, categorieParCarte, tolerances);
    }

    /// <summary>Cartes filtrées par tenant SAGE, plage de matricules (MatriculeRange) et département.</summary>
    private async Task<List<CardPaie>> ChargerCartesAsync(ReportFilter filter, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        var periodRequest = new PeriodRequest(filter.MatriculeFrom?.Trim(), filter.MatriculeTo?.Trim(), filter.From, filter.To, filter.Departement);
        return (await _uow.Repository<CardPaie>().ListAsync(c => c.SageBdd == sageDb, ct))
            .Where(c => MatriculeRange.Contains(c.SageMatricule?.Trim(), periodRequest))
            .Where(c => string.IsNullOrWhiteSpace(filter.Departement)
                        || string.Equals(c.Departement?.Trim(), filter.Departement.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Congés SAGE chargés une seule fois pour le tenant (C6 : avertissement si indisponibles).</summary>
    private async Task<Dictionary<string, List<LeaveDay>>> ChargerCongesAsync(CancellationToken ct)
    {
        try
        {
            return await _leaves.LoadAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Impossible de charger les congés SAGE pour les rapports (base: {SageDb}).", _tenant.SageDb);
            return new Dictionary<string, List<LeaveDay>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Corrections de la plage [from, veille] restreintes aux salariés retenus (C5).</summary>
    private async Task<List<HeureCorrigee>> ChargerCorrectionsAsync(
        List<CardPaie> cartes, DateTime from, DateTime finExclusive, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        var clesCartes = cartes
            .Select(c => MatriculeIdentity.Cle(c.SageMatricule))
            .Where(c => !string.IsNullOrEmpty(c))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (await _uow.Repository<HeureCorrigee>().ListAsync(h =>
                h.BddSage == sageDb && h.DatePointageIn >= from && h.DatePointageIn < finExclusive, ct))
            .Where(h => clesCartes.Contains(MatriculeIdentity.Cle(h.MatriculeSage)))
            .ToList();
    }

    /// <summary>
    /// Plannings de la plage [from, veille]. Les jours OFF sont conservés : ils sont interprétés par
    /// les règles (un jour OFF n'est jamais une absence ni un retard).
    /// </summary>
    private async Task<List<PlanningSalarie>> ChargerPlanningsAsync(
        List<CardPaie> cartes, DateTime from, DateTime finExclusive, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        var idsalSet = cartes.Select(c => c.Id).ToHashSet();
        return (await _uow.Repository<PlanningSalarie>().ListAsync(p =>
                p.BddSage == sageDb && p.DateP >= from && p.DateP < finExclusive, ct))
            .Where(p => idsalSet.Contains(p.IdSal))
            .ToList();
    }
}
