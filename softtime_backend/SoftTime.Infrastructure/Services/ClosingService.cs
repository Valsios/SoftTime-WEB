using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

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
        if (request.ReuseCached && _cache.TryGet(cle, maintenant, out var preparee))
            return ClosingComputation.Assembler(preparee, request);

        preparee = ClosingComputation.Preparer(await ChargerAsync(request, ct), request);
        _cache.Set(cle, preparee, maintenant, TimeSpan.FromSeconds(_reglages.CacheSeconds));
        return ClosingComputation.Assembler(preparee, request);
    }

    /// <summary>
    /// Chargement ensembliste de la période demandée (et de la période précédente seulement si la
    /// comparaison est demandée). Le filtre département s'applique aux cartes, puis toutes les autres
    /// données sont restreintes aux salariés retenus avant le calcul. Planning, pointages, corrections
    /// et semaines validées sont projetés sur les seules colonnes utiles aux règles : ces objets ne
    /// sont pas suivis par le contexte (lecture seule, mémoire réduite).
    /// </summary>
    internal async Task<ClosingData> ChargerAsync(ClosingAnalysisRequest request, CancellationToken ct)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var pointeuseDb = _tenant.PointeuseDb;

        var (debutPrecedent, _) = ClosingComputation.PeriodePrecedente(request.From, request.To);
        var debut = request.IncludeComparison ? debutPrecedent : request.From.Date;
        var fin = request.To.Date;

        var clock = (await _uow.Repository<ParametrePointeuse>().ListAsync(_ => true, ct)).FirstOrDefault()
            ?? throw new DomainException("Paramètre pointeuse (T_CLOCK) manquant.");

        var cartes = (await _uow.Repository<CardPaie>().ListAsync(c => c.SageBdd == sageDb, ct))
            .Where(c => CorrespondPointeuse(c.PointeuseBdd, pointeuseDb))
            .Where(c => !string.IsNullOrWhiteSpace(c.SageMatricule))
            .Where(c => string.IsNullOrWhiteSpace(request.Departement)
                        || string.Equals(c.Departement?.Trim(), request.Departement.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        var idsCartes = cartes.Select(c => c.Id).ToHashSet();
        var cles = cartes.Select(c => MatriculeIdentity.Cle(c.SageMatricule)).ToHashSet(StringComparer.Ordinal);

        var feries = (await _uow.Repository<JourFerie>().ListAsync(f => f.BddSage == sageDb, ct))
            .Where(f => f.Date.HasValue)
            .Select(f => f.Date!.Value.Date)
            .ToHashSet();

        if (cartes.Count == 0)
            return new ClosingData(clock.MultiPoint == true, cartes,
                new List<PlanningSalarie>(), new List<Pointage>(), new List<HeureCorrigee>(),
                new List<HsValidee>(), feries, new Dictionary<string, List<LeaveDay>>(), Today());

        // Marge d'un jour de planning de chaque côté (postes de nuit), comme le pré-traitement.
        var planDebut = debut.AddDays(-1);
        var planFin = fin.AddDays(1);
        var repoPlanning = _uow.Repository<PlanningSalarie>();
        var plannings = (await repoPlanning.ToListAsync(repoPlanning.Query()
                .Where(p => p.BddSage == sageDb && p.DateP >= planDebut && p.DateP <= planFin)
                .OrderBy(p => p.Id)
                .Select(p => new PlanningSalarie
                {
                    Id = p.Id, IdSal = p.IdSal, DateP = p.DateP, Ha = p.Ha, Hd = p.Hd, Pause = p.Pause, OffP = p.OffP
                }), ct))
            .Where(p => p.DateP.HasValue && idsCartes.Contains(p.IdSal))
            .ToList();

        // Sorties de nuit : les pointages du lendemain de la fin de période sont aussi chargés.
        var pointagesFinExclusive = fin.AddDays(2);
        var repoPointage = _uow.Repository<Pointage>();
        var pointages = (await repoPointage.ToListAsync(repoPointage.Query()
                .Where(p => p.NomBddSage == sageDb && p.DatePointage >= debut && p.DatePointage < pointagesFinExclusive)
                .OrderBy(p => p.IdPointage)
                .Select(p => new Pointage
                {
                    IdPointage = p.IdPointage, MatriculeSage = p.MatriculeSage, DatePointage = p.DatePointage,
                    HeurePointage = p.HeurePointage, NomBddPointeuse = p.NomBddPointeuse
                }), ct))
            .Where(p => CorrespondPointeuse(p.NomBddPointeuse, pointeuseDb))
            .Where(p => cles.Contains(MatriculeIdentity.Cle(p.MatriculeSage)))
            .ToList();

        var correctionsFinExclusive = fin.AddDays(1);
        var repoCorrection = _uow.Repository<HeureCorrigee>();
        var corrections = (await repoCorrection.ToListAsync(repoCorrection.Query()
                .Where(h => h.BddSage == sageDb && h.DatePointageIn >= debut && h.DatePointageIn < correctionsFinExclusive)
                .OrderBy(h => h.Id)
                .Select(h => new HeureCorrigee
                {
                    Id = h.Id, MatriculeSage = h.MatriculeSage, DatePointageIn = h.DatePointageIn,
                    HEntree = h.HEntree, HSortie = h.HSortie, Hps = h.Hps, Hpe = h.Hpe,
                    AbsenceAm = h.AbsenceAm, AbsencePm = h.AbsencePm, IntituleAbsence = h.IntituleAbsence,
                    Ferier = h.Ferier, FerierAm = h.FerierAm, FerierPm = h.FerierPm, MFeries = h.MFeries,
                    Origine = h.Origine
                }), ct))
            .Where(h => cles.Contains(MatriculeIdentity.Cle(h.MatriculeSage)))
            .ToList();

        var lundiDebut = debut.AddDays(-(((int)debut.DayOfWeek + 6) % 7));
        var repoSemaine = _uow.Repository<HsValidee>();
        var semainesValidees = (await repoSemaine.ToListAsync(repoSemaine.Query()
                .Where(v => v.BddSage == sageDb && v.ValidationHs == true && v.DateDeb >= lundiDebut && v.DateDeb <= fin)
                .OrderBy(v => v.Id)
                .Select(v => new HsValidee { Id = v.Id, Matricules = v.Matricules, DateDeb = v.DateDeb, ValidationHs = v.ValidationHs }), ct))
            .Where(v => cles.Contains(MatriculeIdentity.Cle(v.Matricules)))
            .ToList();

        var congesTenant = await _leaves.LoadAsync(ct);
        var conges = congesTenant
            .Where(kv => cles.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        return new ClosingData(clock.MultiPoint == true, cartes, plannings, pointages, corrections,
            semainesValidees, feries, conges, Today());
    }

    private DateTime Today() => _horloge.GetLocalNow().Date;

    /// <summary>Filtre tenant pointeuse, en tolérant les lignes héritées sans base renseignée.</summary>
    private static bool CorrespondPointeuse(string? valeur, string pointeuseDb) =>
        string.IsNullOrWhiteSpace(valeur)
        || string.Equals(valeur.Trim(), pointeuseDb.Trim(), StringComparison.OrdinalIgnoreCase);
}
