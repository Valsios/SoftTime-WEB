using System.Diagnostics;

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
    /// <summary>
    /// Au-delà, aucune réduction SQL par liste (identifiants de cartes, matricules) : la lecture reste celle d'avant et le
    /// filtrage se fait en mémoire, avec le même résultat. Évite les requêtes à très longue liste de valeurs.
    /// </summary>
    internal const int TailleMaxListeSql = 1000;

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
    public Task<ReportContext?> ChargerContexteAsync(ReportFilter filter, CancellationToken ct)
        => ChargerContexteAsync(filter, avecTolerances: true, ct);

    /// <inheritdoc />
    public async Task<ReportContext?> ChargerContexteAsync(ReportFilter filter, bool avecTolerances, CancellationToken ct)
    {
        var from = filter.From.Date;
        var hier = DateTime.Today.AddDays(-1);
        var fin = filter.To.Date < hier ? filter.To.Date : hier;
        if (from > fin)
            return null;

        var chrono = Stopwatch.StartNew();
        var cartes = await ChargerCartesAsync(filter, ct);
        var cartesMs = chrono.ElapsedMilliseconds;
        if (cartes.Count == 0)
            return null;

        var sageDb = _tenant.SageDb;
        chrono.Restart();
        var feries = (await _uow.Repository<JourFerie>().ListReadOnlyAsync(f => f.BddSage == sageDb, ct))
            .Where(f => f.Date.HasValue)
            .Select(f => f.Date!.Value.Date)
            .ToHashSet();
        var feriesMs = chrono.ElapsedMilliseconds;

        chrono.Restart();
        var (conges, congesDisponibles) = await ChargerCongesAsync(ct);
        var congesMs = chrono.ElapsedMilliseconds;

        // Un filtre matricules ou département renseigné réduit les lectures SQL ; sans filtre, rien n'est ajouté.
        var filtre = FiltreRenseigne(filter);
        chrono.Restart();
        var (corrections, correctionsReduites) = await ChargerCorrectionsAsync(cartes, from, fin.AddDays(1), filtre, ct);
        var correctionsMs = chrono.ElapsedMilliseconds;
        chrono.Restart();
        var (plannings, planningsReduits) = await ChargerPlanningsAsync(cartes, from, fin.AddDays(1), filtre, ct);
        var planningsMs = chrono.ElapsedMilliseconds;

        // Catégorie du salarié (T_CAT_SAL référence T_CARDPAIE.ID) et tolérances de catégorie : réservées à l'état des retards.
        chrono.Restart();
        IReadOnlyDictionary<int, int> categorieParCarte = new Dictionary<int, int>();
        IReadOnlyList<ToleranceRetard> tolerances = Array.Empty<ToleranceRetard>();
        if (avecTolerances)
        {
            categorieParCarte = (await _uow.Repository<CategorieSalarie>().ListReadOnlyAsync(a => a.BddSage == sageDb, ct))
                .GroupBy(a => a.MatriculeSage)
                .ToDictionary(g => g.Key, g => g.First().IdCateg);
            tolerances = await _uow.Repository<ToleranceRetard>().ListReadOnlyAsync(_ => true, ct);
        }
        var autresMs = chrono.ElapsedMilliseconds;

        var mesures = new ReportLoadStats(
            cartesMs, cartes.Count, feriesMs, congesMs, correctionsMs, corrections.Count, planningsMs, plannings.Count, autresMs,
            FiltresPousses: correctionsReduites || planningsReduits);
        return new ReportContext(cartes, feries, conges, corrections, plannings, categorieParCarte, tolerances, congesDisponibles, mesures);
    }

    /// <inheritdoc />
    public async Task<AbsenceCodesComptes> ChargerCodesAbsenceAsync(CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        return PayrollOptions.AbsenceCodes(await _uow.Repository<PaieOption>().ListReadOnlyAsync(o => o.BddSage == sageDb, ct));
    }

    private static bool FiltreRenseigne(ReportFilter filter)
        => !string.IsNullOrWhiteSpace(filter.MatriculeFrom)
           || !string.IsNullOrWhiteSpace(filter.MatriculeTo)
           || !string.IsNullOrWhiteSpace(filter.Departement);

    /// <summary>
    /// Cartes filtrées par tenant SAGE, plage de matricules (MatriculeRange) et département. Le filtre reste en mémoire :
    /// il compare sans tenir compte de la casse ni des espaces, ce que SQL ne reproduirait pas à l'identique, et la table
    /// est petite.
    /// </summary>
    private async Task<List<CardPaie>> ChargerCartesAsync(ReportFilter filter, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        var periodRequest = new PeriodRequest(filter.MatriculeFrom?.Trim(), filter.MatriculeTo?.Trim(), filter.From, filter.To, filter.Departement);
        return (await _uow.Repository<CardPaie>().ListReadOnlyAsync(c => c.SageBdd == sageDb, ct))
            .Where(c => MatriculeRange.Contains(c.SageMatricule?.Trim(), periodRequest))
            .Where(c => string.IsNullOrWhiteSpace(filter.Departement)
                        || string.Equals(c.Departement?.Trim(), filter.Departement.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Congés SAGE chargés une seule fois pour le tenant (C6 : avertissement si indisponibles). En cas d'échec, les congés
    /// sont vides et le contexte le signale (<see cref="ReportContext.CongesDisponibles"/>) pour les états qui en dépendent.
    /// </summary>
    private async Task<(Dictionary<string, List<LeaveDay>> Conges, bool Disponibles)> ChargerCongesAsync(CancellationToken ct)
    {
        try
        {
            return (await _leaves.LoadAsync(ct), true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Impossible de charger les congés SAGE pour les rapports (base: {SageDb}).", _tenant.SageDb);
            return (new Dictionary<string, List<LeaveDay>>(StringComparer.OrdinalIgnoreCase), false);
        }
    }

    /// <summary>
    /// Corrections de la plage [from, veille] restreintes aux salariés retenus (C5). Le rattachement se fait par clé de
    /// matricule (zéros de tête ignorés) : ce filtre en mémoire reste toujours appliqué. Avec un filtre renseigné, la lecture
    /// SQL est d'abord réduite aux matricules réellement présents dans la plage dont la clé est celle d'une carte retenue
    /// (résultat identique, moins de lignes lues) ; sans filtre ou trop de valeurs, la lecture est celle d'avant.
    /// </summary>
    private async Task<(List<HeureCorrigee> Lignes, bool Reduit)> ChargerCorrectionsAsync(
        List<CardPaie> cartes, DateTime from, DateTime finExclusive, bool filtre, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        var repo = _uow.Repository<HeureCorrigee>();
        var clesCartes = cartes
            .Select(c => MatriculeIdentity.Cle(c.SageMatricule))
            .Where(c => !string.IsNullOrEmpty(c))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<string>? presents = null;
        if (filtre)
        {
            var distincts = await repo.ToListAsync(
                repo.QueryReadOnly()
                    .Where(h => h.BddSage == sageDb && h.DatePointageIn >= from && h.DatePointageIn < finExclusive)
                    .Select(h => h.MatriculeSage)
                    .Distinct(),
                ct);
            presents = distincts
                .Where(m => m is not null && clesCartes.Contains(MatriculeIdentity.Cle(m)))
                .Select(m => m!)
                .ToList();
        }

        var reduit = presents is { Count: <= TailleMaxListeSql };
        var lues = reduit
            ? await repo.ListReadOnlyAsync(h =>
                h.BddSage == sageDb && h.DatePointageIn >= from && h.DatePointageIn < finExclusive
                && presents!.Contains(h.MatriculeSage), ct)
            : await repo.ListReadOnlyAsync(h =>
                h.BddSage == sageDb && h.DatePointageIn >= from && h.DatePointageIn < finExclusive, ct);

        return (lues.Where(h => clesCartes.Contains(MatriculeIdentity.Cle(h.MatriculeSage))).ToList(), reduit);
    }

    /// <summary>
    /// Plannings de la plage [from, veille]. Les jours OFF sont conservés : ils sont interprétés par
    /// les règles (un jour OFF n'est jamais une absence ni un retard). Avec un filtre renseigné, la lecture SQL est réduite
    /// aux cartes retenues (même critère que le filtre en mémoire, qui reste appliqué).
    /// </summary>
    private async Task<(List<PlanningSalarie> Lignes, bool Reduit)> ChargerPlanningsAsync(
        List<CardPaie> cartes, DateTime from, DateTime finExclusive, bool filtre, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        var repo = _uow.Repository<PlanningSalarie>();
        var idsal = cartes.Select(c => c.Id).Distinct().ToList();
        var idsalSet = idsal.ToHashSet();

        var reduit = filtre && idsal.Count <= TailleMaxListeSql;
        var lues = reduit
            ? await repo.ListReadOnlyAsync(p =>
                p.BddSage == sageDb && p.DateP >= from && p.DateP < finExclusive && idsal.Contains(p.IdSal), ct)
            : await repo.ListReadOnlyAsync(p =>
                p.BddSage == sageDb && p.DateP >= from && p.DateP < finExclusive, ct);
        return (lues.Where(p => idsalSet.Contains(p.IdSal)).ToList(), reduit);
    }
}
