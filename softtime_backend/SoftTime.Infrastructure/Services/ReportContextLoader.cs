using Microsoft.Extensions.Logging;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

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
        var sageDb = _tenant.SageDb;
        var from = filter.From.Date;
        var hier = DateTime.Today.AddDays(-1);
        var fin = filter.To.Date < hier ? filter.To.Date : hier;
        if (from > fin)
            return null;

        var borneFinExclusive = fin.AddDays(1);

        // 1. Cartes filtrées par tenant SAGE, plage de matricules (MatriculeRange) et Département
        var periodRequest = new PeriodRequest(filter.MatriculeFrom?.Trim(), filter.MatriculeTo?.Trim(), filter.From, filter.To, filter.Departement);
        var cartes = (await _uow.Repository<CardPaie>().ListAsync(c => c.SageBdd == sageDb, ct))
            .Where(c => MatriculeRange.Contains(c.SageMatricule?.Trim(), periodRequest))
            .Where(c => string.IsNullOrWhiteSpace(filter.Departement)
                        || string.Equals(c.Departement?.Trim(), filter.Departement.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (cartes.Count == 0)
            return null;

        var idsalSet = cartes.Select(c => c.Id).ToHashSet();
        var clesCartes = cartes
            .Select(c => MatriculeIdentity.Cle(c.SageMatricule))
            .Where(c => !string.IsNullOrEmpty(c))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 2. Fériés de la base SAGE
        var feries = (await _uow.Repository<JourFerie>().ListAsync(f => f.BddSage == sageDb, ct))
            .Where(f => f.Date.HasValue)
            .Select(f => f.Date!.Value.Date)
            .ToHashSet();

        // 3. Congés SAGE chargés une seule fois pour le tenant (C6 : log warning si indisponible)
        Dictionary<string, List<LeaveDay>> conges = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            conges = await _leaves.LoadAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Impossible de charger les congés SAGE pour les rapports (base: {SageDb}).", sageDb);
        }

        // 4. Corrections de la plage [from, veille] restreintes aux salariés retenus (C5)
        var corrections = (await _uow.Repository<HeureCorrigee>().ListAsync(h =>
            h.BddSage == sageDb
            && h.DatePointageIn >= from && h.DatePointageIn < borneFinExclusive, ct))
            .Where(h => clesCartes.Contains(MatriculeIdentity.Cle(h.MatriculeSage)))
            .ToList();

        // 5. Plannings de la plage [from, veille]. Les jours OFF sont conservés ici : ils sont
        // interprétés par les règles (un jour OFF n'est jamais une absence ni un retard).
        var plannings = (await _uow.Repository<PlanningSalarie>().ListAsync(p =>
            p.BddSage == sageDb
            && p.DateP >= from && p.DateP < borneFinExclusive, ct))
            .Where(p => idsalSet.Contains(p.IdSal))
            .ToList();

        // 6. Catégorie du salarié (T_CAT_SAL référence T_CARDPAIE.ID) et tolérances de catégorie
        var categorieParCarte = (await _uow.Repository<CategorieSalarie>().ListAsync(a => a.BddSage == sageDb, ct))
            .GroupBy(a => a.MatriculeSage)
            .ToDictionary(g => g.Key, g => g.First().IdCateg);
        var tolerances = await _uow.Repository<ToleranceRetard>().ListAsync(_ => true, ct);

        return new ReportContext(cartes, feries, conges, corrections, plannings, categorieParCarte, tolerances);
    }
}
