using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.Sage;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

/// <summary>
/// Calcul et validation des heures supplémentaires (portage de <c>Frm_Validations</c>,
/// <c>Frm_CalculHS</c> et des méthodes <c>setHSSemaine</c> / <c>setHSEXO130EXO150I130I150</c>
/// du desktop). Les règles métier pures sont dans <see cref="OvertimeCalculator"/> ; ce service
/// charge le contexte, applique le périmètre puis persiste <c>T_HS_VALIDER</c>, <c>T_HSSemaine</c>
/// et <c>T_HSExoImp</c>.
/// </summary>
public class OvertimeService
{
    private readonly IUnitOfWork _uow;
    private readonly TenantConnectionService _tenant;
    private readonly IExternalSourceService _externalSource;
    private readonly PayrollWriterResolver _payrollWriters;
    private readonly HistoryArchiveService _history;
    private readonly SageLeaveCalendar _leaves;
    private readonly ICurrentUser? _user;

    public OvertimeService(IUnitOfWork uow, TenantConnectionService tenant, IExternalSourceService externalSource, PayrollWriterResolver payrollWriters, HistoryArchiveService history, SageLeaveCalendar leaves)
        : this(uow, tenant, externalSource, payrollWriters, history, leaves, null)
    {
    }

    public OvertimeService(
        IUnitOfWork uow,
        TenantConnectionService tenant,
        IExternalSourceService externalSource,
        PayrollWriterResolver payrollWriters,
        HistoryArchiveService history,
        SageLeaveCalendar leaves,
        ICurrentUser? user)
    {
        _uow = uow;
        _tenant = tenant;
        _externalSource = externalSource;
        _payrollWriters = payrollWriters;
        _history = history;
        _leaves = leaves;
        _user = user;
    }

    public async Task<IReadOnlyList<WeeklyHsDto>> PreviewWeekAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        EnsureMondayToSunday(request);
        return await ComputeWeekAsync(request, persistNonValidated: true, ct);
    }

    /// <summary>
    /// Aperçu paginé de la validation semaine. Lecture seule : contrairement à
    /// <see cref="PreviewWeekAsync"/>, ne persiste pas les lignes non validées, afin de ne pas
    /// écrire à chaque changement de page. Les semaines déjà validées sont renvoyées telles quelles
    /// (elles ne sont plus recalculées) pour pouvoir les annuler depuis l'écran.
    /// </summary>
    public async Task<PagedResult<WeeklyHsDto>> PreviewWeekPagedAsync(
        PeriodRequest request, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        EnsureMondayToSunday(request);
        var rows = await ComputeWeekAsync(request, persistNonValidated: false, ct);
        return PagedLists.Page(rows, page, pageSize);
    }

    /// <summary>
    /// Valide (ou annule la validation de) la semaine pour les salariés sélectionnés.
    /// La validation fige la semaine (<c>VALIDER_HS</c>), l'annulation la rouvre (synthèse §5).
    /// </summary>
    public async Task ValidateWeekAsync(WeeklyValidationRequest request, bool validate, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        EnsureMondayToSunday(request);

        var selected = new HashSet<string>(
            (request.SelectedMatricules ?? Array.Empty<string>())
                .Select(MatriculeIdentity.Cle)
                .Where(cle => cle.Length > 0),
            StringComparer.Ordinal);
        if (selected.Count == 0)
            return;

        var sageDb = _tenant.SageDb;
        var from = request.From.Date;
        var to = request.To.Date;
        var hsRepo = _uow.Repository<T_HS_VALIDER>();
        var corrRepo = _uow.Repository<T_HEURECORRIGER>();

        // Un recalcul est nécessaire pour obtenir les valeurs à valider (les semaines déjà validées
        // sont exclues du calcul et donc absentes de `rows`).
        if (validate)
            await ComputeWeekAsync(request, persistNonValidated: true, ct);

        var lignes = (await hsRepo.ListAsync(x => x.BDD_SAGE == sageDb && x.DATE_DEB == from, ct))
            .Where(x => !string.IsNullOrEmpty(x.MATRICULES) && selected.Contains(MatriculeIdentity.Cle(x.MATRICULES)))
            .GroupBy(x => MatriculeIdentity.Cle(x.MATRICULES))
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.ID).First(), StringComparer.Ordinal);

        var corrections = await corrRepo.ListAsync(h => h.BDD_SAGE == sageDb
            && h.DATE_POINTAGE_IN >= from && h.DATE_POINTAGE_IN <= to, ct);
        var correctionsParCle = corrections
            .Where(h => !string.IsNullOrEmpty(h.MATRICULE_SAGE))
            .GroupBy(h => MatriculeIdentity.Cle(h.MATRICULE_SAGE))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var (cle, ligne) in lignes)
        {
            if (validate && ligne.VALIDATION_HS == true)
                continue;
            if (!validate && ligne.VALIDATION_HS != true)
                continue;

            ligne.VALIDATION_HS = validate;
            ligne.DATE_FIN = to;
            hsRepo.Update(ligne);

            if (correctionsParCle.TryGetValue(cle, out var heures))
            {
                foreach (var heure in heures)
                {
                    heure.VALIDER_HS = validate;
                    corrRepo.Update(heure);
                }
            }
        }

        await _uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<HsExoDto>> ListExoAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        EnsureMaxOneMonth(request);
        var cards = await TargetCards(request, ct);
        var targetMatricules = cards.Select(c => c.SAGE_MATRICULE).Where(m => !string.IsNullOrEmpty(m)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var list = await _uow.Repository<T_HSExoImp>().ListAsync(h =>
            h.BDD_SAGE == _tenant.SageDb
            && h.PeriodeDebut <= request.To.Date
            && h.PeriodeFin >= request.From.Date
            && (string.IsNullOrEmpty(request.MatriculeFrom) || string.Compare(h.Matricule, request.MatriculeFrom) >= 0)
            && (string.IsNullOrEmpty(request.MatriculeTo) || string.Compare(h.Matricule, request.MatriculeTo) <= 0), ct);

        if (!string.IsNullOrEmpty(request.Departement) || !string.IsNullOrEmpty(request.Branche))
        {
            list = list.Where(h => h.Matricule != null && targetMatricules.Contains(h.Matricule)).ToList();
        }

        return list
            .OrderBy(h => h.Matricule)
            .Select(h => new HsExoDto(h.OID, h.Matricule, h.NumSal, h.EXO130, h.EXO150, h.I130, h.I150, h.FERIE, h.NUIT, h.DIM, h.RETARD, h.ABSENCE))
            .ToList();
    }

    /// <summary>
    /// Liste paginée des cumuls EXO/IMPO. Le filtre est traduit en SQL ; le filtre
    /// branche/département est résolu via les cartes cibles (même sémantique que
    /// <see cref="ListExoAsync"/>). L'endpoint non paginé reste utilisé pour l'export.
    /// </summary>
    public async Task<PagedResult<HsExoDto>> ListExoPagedAsync(
        PeriodRequest request, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        EnsureMaxOneMonth(request);
        var sageDb = _tenant.SageDb;
        var from = request.From.Date;
        var to = request.To.Date;
        var matriculeFrom = request.MatriculeFrom;
        var matriculeTo = request.MatriculeTo;
        var repo = _uow.Repository<T_HSExoImp>();

        static HsExoDto Map(T_HSExoImp h) => new(
            h.OID, h.Matricule, h.NumSal, h.EXO130, h.EXO150, h.I130, h.I150,
            h.FERIE, h.NUIT, h.DIM, h.RETARD, h.ABSENCE);

        if (string.IsNullOrEmpty(request.Departement) && string.IsNullOrEmpty(request.Branche))
        {
            return await PagedLists.PageAsync(
                repo,
                h => h.BDD_SAGE == sageDb
                     && h.PeriodeDebut <= to
                     && h.PeriodeFin >= from
                     && (string.IsNullOrEmpty(matriculeFrom) || string.Compare(h.Matricule, matriculeFrom) >= 0)
                     && (string.IsNullOrEmpty(matriculeTo) || string.Compare(h.Matricule, matriculeTo) <= 0),
                q => q.OrderBy(h => h.Matricule).ThenBy(h => h.OID),
                page,
                pageSize,
                Map,
                ct);
        }

        var cards = await TargetCards(request, ct);
        var targetMatricules = cards
            .Select(c => c.SAGE_MATRICULE)
            .Where(m => !string.IsNullOrEmpty(m))
            .Select(m => m!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return await PagedLists.PageAsync(
            repo,
            h => h.BDD_SAGE == sageDb
                 && h.PeriodeDebut <= to
                 && h.PeriodeFin >= from
                 && (string.IsNullOrEmpty(matriculeFrom) || string.Compare(h.Matricule, matriculeFrom) >= 0)
                 && (string.IsNullOrEmpty(matriculeTo) || string.Compare(h.Matricule, matriculeTo) <= 0)
                 && targetMatricules.Contains(h.Matricule!),
            q => q.OrderBy(h => h.Matricule).ThenBy(h => h.OID),
            page,
            pageSize,
            Map,
            ct);
    }

    /// <summary>
    /// Étapes 3 et 4 de la chaîne (synthèse §6 et §7) : reconstruit <c>T_HSSemaine</c> à partir des
    /// semaines <b>validées</b> (8 h à 130 %, au-delà à 150 %), puis agrège les compteurs de la période
    /// dans <c>T_HSExoImp</c> avec le plafond de 20 h exonérées.
    /// </summary>
    public async Task<IReadOnlyList<HsExoDto>> CalculateExoImpoAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        EnsureMaxOneMonth(request);
        var sageDb = _tenant.SageDb;
        var from = request.From.Date;
        var to = request.To.Date;

        var cards = (await TargetCards(request, ct))
            .OrderBy(c => c.SAGE_MATRICULE, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var targetCles = cards
            .Select(c => MatriculeIdentity.Cle(c.SAGE_MATRICULE))
            .Where(cle => cle.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var numSalByMat = await ChargerNumSalAsync(ct);

        // Semaines validées qui chevauchent la période : une période démarrant un mercredi inclut
        // donc sa première semaine (le desktop passait par T_SemainePeriode).
        var debutSemaineMin = from.AddDays(-6);
        var validees = (await _uow.Repository<T_HS_VALIDER>().ListAsync(
                h => h.BDD_SAGE == sageDb
                     && h.VALIDATION_HS == true
                     && h.DATE_DEB != null
                     && h.DATE_DEB >= debutSemaineMin
                     && h.DATE_DEB <= to, ct))
            .Where(h => !string.IsNullOrEmpty(h.MATRICULES) && targetCles.Contains(MatriculeIdentity.Cle(h.MATRICULES)))
            .GroupBy(h => MatriculeIdentity.Cle(h.MATRICULES), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.DATE_DEB).ToList(), StringComparer.Ordinal);

        // Absence de la période : calculée sur le planning, indépendamment de la validation.
        var ctx = await ChargerContexteAsync(request, avecExistantes: false, ct);
        var absences = cards.ToDictionary(
            c => MatriculeIdentity.Cle(c.SAGE_MATRICULE),
            c => Arrondir(ConstruireJours(ctx, c, from, to).Sum(j => OvertimeCalculator.CalculerJour(Configurer(ctx, c), j).Absence)),
            StringComparer.Ordinal);

        var semaineRepo = _uow.Repository<T_HSSemaine>();
        var exoRepo = _uow.Repository<T_HSExoImp>();
        var anciennesSemaines = (await semaineRepo.ListAsync(
                h => h.BDD_SAGE == sageDb && h.PeriodeDebut == from && h.PeriodeFin == to, ct))
            .Where(h => h.Matricule != null && targetCles.Contains(MatriculeIdentity.Cle(h.Matricule)))
            .ToList();
        var anciennesExo = (await exoRepo.ListAsync(
                h => h.BDD_SAGE == sageDb && h.PeriodeDebut == from && h.PeriodeFin == to, ct))
            .Where(h => h.Matricule != null && targetCles.Contains(MatriculeIdentity.Cle(h.Matricule)))
            .ToList();

        var nouvellesSemaines = new List<T_HSSemaine>();
        var nouvellesExo = new List<T_HSExoImp>();

        foreach (var card in cards)
        {
            var cle = MatriculeIdentity.Cle(card.SAGE_MATRICULE);
            var semaines = validees.GetValueOrDefault(cle) ?? new List<T_HS_VALIDER>();
            var splits = semaines.Select(w => OvertimeRules.SplitWeekly(w.HS ?? 0)).ToList();
            var numSal = numSalByMat.TryGetValue(cle, out var n) ? n : (int?)null;

            var rang = 1;
            foreach (var split in splits)
            {
                nouvellesSemaines.Add(new T_HSSemaine
                {
                    Matricule = card.SAGE_MATRICULE,
                    SalNum = numSal,
                    TotalHeure = split.Hs,
                    HS = split.Hs,
                    HS1 = split.Hs1,
                    HS2 = split.Hs2,
                    PeriodeDebut = from,
                    PeriodeFin = to,
                    Semaine = rang++,
                    BDD_SAGE = sageDb,
                    BDD_POINTEUSE = _tenant.PointeuseDb
                });
            }

            var exo = OvertimeRules.AllocateExempt(splits);
            var absence = absences.GetValueOrDefault(cle);
            if (semaines.Count == 0 && absence == 0m)
                continue;

            nouvellesExo.Add(new T_HSExoImp
            {
                Matricule = card.SAGE_MATRICULE,
                NumSal = numSal,
                EXO130 = exo.Exo130,
                EXO150 = exo.Exo150,
                I130 = exo.Impo130,
                I150 = exo.Impo150,
                PeriodeDebut = from,
                PeriodeFin = to,
                NUIT = Arrondir(semaines.Sum(x => x.NUIT ?? 0m)),
                DIM = Arrondir(semaines.Sum(x => x.DIMANCHE ?? 0m)),
                FERIE = Arrondir(semaines.Sum(x => x.FERIES ?? 0m)),
                RETARD = Arrondir(semaines.Sum(x => x.RETARD ?? 0m)),
                ABSENCE = absence,
                BDD_SAGE = sageDb,
                BDD_POINTEUSE = _tenant.PointeuseDb
            });
        }

        if (anciennesSemaines.Count > 0)
            semaineRepo.RemoveRange(anciennesSemaines);
        if (anciennesExo.Count > 0)
            exoRepo.RemoveRange(anciennesExo);
        if (nouvellesSemaines.Count > 0)
            await semaineRepo.AddRangeAsync(nouvellesSemaines, ct);
        if (nouvellesExo.Count > 0)
            await exoRepo.AddRangeAsync(nouvellesExo, ct);
        await _uow.SaveChangesAsync(ct);

        return nouvellesExo
            .Select(e => new HsExoDto(e.OID, e.Matricule, e.NumSal, e.EXO130, e.EXO150, e.I130, e.I150,
                e.FERIE, e.NUIT, e.DIM, e.RETARD, e.ABSENCE))
            .ToList();
    }

    public async Task PurgeHsAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        EnsureMaxOneMonth(request);
        var sem = await _uow.Repository<T_HSSemaine>().ListAsync(h =>
            h.BDD_SAGE == _tenant.SageDb && h.PeriodeDebut == request.From.Date && h.PeriodeFin == request.To.Date
            && (string.IsNullOrEmpty(request.MatriculeFrom) || string.Compare(h.Matricule, request.MatriculeFrom) >= 0)
            && (string.IsNullOrEmpty(request.MatriculeTo) || string.Compare(h.Matricule, request.MatriculeTo) <= 0), ct);
        var exo = await _uow.Repository<T_HSExoImp>().ListAsync(h =>
            h.BDD_SAGE == _tenant.SageDb && h.PeriodeDebut == request.From.Date && h.PeriodeFin == request.To.Date
            && (string.IsNullOrEmpty(request.MatriculeFrom) || string.Compare(h.Matricule, request.MatriculeFrom) >= 0)
            && (string.IsNullOrEmpty(request.MatriculeTo) || string.Compare(h.Matricule, request.MatriculeTo) <= 0), ct);

        if (!string.IsNullOrEmpty(request.Departement) || !string.IsNullOrEmpty(request.Branche))
        {
            var cards = await TargetCards(request, ct);
            var targetMatricules = cards.Select(c => c.SAGE_MATRICULE).Where(m => !string.IsNullOrEmpty(m)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            sem = sem.Where(h => h.Matricule != null && targetMatricules.Contains(h.Matricule)).ToList();
            exo = exo.Where(h => h.Matricule != null && targetMatricules.Contains(h.Matricule)).ToList();
        }

        _uow.Repository<T_HSSemaine>().RemoveRange(sem);
        await _history.ArchiveHsAsync(exo, ct);
        _uow.Repository<T_HSExoImp>().RemoveRange(exo);
        await _uow.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Purge la validation d'une semaine (lundi–dimanche) : supprime les lignes <c>T_HS_VALIDER</c>
    /// et déverrouille les heures corrigées correspondantes. Refuse si le calcul EXO/IMPO existe
    /// encore, pour ne pas laisser des montants payés sans validation.
    /// </summary>
    public async Task<PurgeValidationResultDto> PurgeValidationAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        EnsureMondayToSunday(request);
        var sageDb = _tenant.SageDb;
        var from = request.From.Date;
        var to = request.To.Date;

        var hs = (await _uow.Repository<T_HSExoImp>().ListAsync(
            h => h.BDD_SAGE == sageDb && h.PeriodeDebut == from && h.PeriodeFin == to, ct)).Count;
        if (hs > 0)
            throw new InvalidOperationException(
                "Des calculs EXO/IMPO existent sur la période : purgez d'abord les heures supplémentaires.");

        var repo = _uow.Repository<T_HS_VALIDER>();
        var lignes = (await repo.ListAsync(
                v => v.BDD_SAGE == sageDb && v.DATE_DEB == from && v.DATE_FIN == to, ct))
            .Where(v => MatriculeRange.Contains(v.MATRICULES, request))
            .ToList();
        if (lignes.Count == 0)
            return new PurgeValidationResultDto(0, 0, "Aucune validation à purger sur la période.");

        var corrRepo = _uow.Repository<T_HEURECORRIGER>();
        var corrections = await corrRepo.ListAsync(h => h.BDD_SAGE == sageDb
            && h.DATE_POINTAGE_IN >= from && h.DATE_POINTAGE_IN <= to, ct);
        var correctionsParCle = corrections
            .Where(h => !string.IsNullOrEmpty(h.MATRICULE_SAGE))
            .GroupBy(h => MatriculeIdentity.Cle(h.MATRICULE_SAGE))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var deverrouillees = 0;
        foreach (var ligne in lignes)
        {
            if (!correctionsParCle.TryGetValue(MatriculeIdentity.Cle(ligne.MATRICULES), out var heures))
                continue;
            foreach (var heure in heures)
            {
                if (heure.VALIDER_HS != true)
                    continue;
                heure.VALIDER_HS = false;
                corrRepo.Update(heure);
                deverrouillees++;
            }
        }

        repo.RemoveRange(lignes);
        await _uow.SaveChangesAsync(ct);
        return new PurgeValidationResultDto(lignes.Count, deverrouillees,
            $"{lignes.Count} validation(s) purgée(s), {deverrouillees} heure(s) corrigée(s) déverrouillée(s).");
    }

    // Endpoint historique : conserve pour les integrations existantes, uniquement pour STANDARD.
    public async Task SyncSageAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageRow = await _tenant.GetSageRowAsync(ct);
        if (!string.Equals(sageRow.TYPE_BASE, "STANDARD", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "La synchronisation SAGE est disponible uniquement pour une base STANDARD. Utilisez la synchronisation de paie pour une base AUTRE.");
        await SyncPayrollAsync(request, ct);
    }

    // Ecriture de paie unifiee : le resolveur choisit le writer STANDARD (T_CUMSAL) ou AUTRE (T_PAIE_ECRITURE).
    public async Task<PayrollWriteResultDto> SyncPayrollAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        EnsureMaxOneMonth(request);
        var sageRow = await _tenant.GetSageRowAsync(ct);
        var rows = await _uow.Repository<T_HSExoImp>().ListAsync(h =>
            h.BDD_SAGE == _tenant.SageDb && h.PeriodeDebut == request.From.Date && h.PeriodeFin == request.To.Date, ct);

        if (!string.IsNullOrEmpty(request.Departement) || !string.IsNullOrEmpty(request.Branche))
        {
            var cards = await TargetCards(request, ct);
            var targetMatricules = cards.Select(c => c.SAGE_MATRICULE).Where(m => !string.IsNullOrEmpty(m)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            rows = rows.Where(h => h.Matricule != null && targetMatricules.Contains(h.Matricule)).ToList();
        }

        var dtos = rows
            .Select(h => new HsExoDto(h.OID, h.Matricule, h.NumSal, h.EXO130, h.EXO150, h.I130, h.I150,
                h.FERIE, h.NUIT, h.DIM, h.RETARD, h.ABSENCE))
            .ToList();
        var auditContext = new PayrollSyncContext(request.From.Date, request.To.Date, _user?.Login, Guid.NewGuid());
        return await _payrollWriters.Resolve(sageRow).WriteAsync(sageRow, dtos, auditContext, ct);
    }

    /// <summary>
    /// Calcul hebdomadaire (étape 1 de la chaîne). Les salariés dont la semaine est déjà validée
    /// (<c>VALIDATION_HS = true</c>) sont renvoyés tels quels : leur ligne est la source de vérité
    /// et n'est plus recalculée (elle doit rester visible pour pouvoir annuler la validation).
    /// </summary>
    private async Task<List<WeeklyHsDto>> ComputeWeekAsync(PeriodRequest request, bool persistNonValidated, CancellationToken ct)
    {
        var from = request.From.Date;
        var to = request.To.Date;
        var ctx = await ChargerContexteAsync(request, avecExistantes: true, ct);
        var hsRepo = _uow.Repository<T_HS_VALIDER>();
        var resultat = new List<WeeklyHsDto>(ctx.Cards.Count);

        foreach (var card in ctx.Cards)
        {
            var cle = MatriculeIdentity.Cle(card.SAGE_MATRICULE);
            ctx.Existantes.TryGetValue(cle, out var existante);
            if (existante?.VALIDATION_HS == true)
            {
                resultat.Add(DepuisEntite(existante));
                continue;
            }

            var config = Configurer(ctx, card);
            var semaine = OvertimeCalculator.CalculerSemaine(config, ConstruireJours(ctx, card, from, to));
            if (persistNonValidated)
                existante = await EnregistrerSemaineAsync(hsRepo, ctx, card, existante, semaine, from, to, ct);

            resultat.Add(new WeeklyHsDto(
                existante?.ID ?? 0, card.SAGE_MATRICULE, from, to,
                semaine.Ht, semaine.Hs, semaine.Nuit, semaine.Dimanche, semaine.Ferie,
                semaine.Retard, semaine.Absence, Validated: false));
        }

        if (persistNonValidated)
            await _uow.SaveChangesAsync(ct);
        return resultat;
    }

    private static WeeklyHsDto DepuisEntite(T_HS_VALIDER ligne)
        => new(ligne.ID, ligne.MATRICULES, ligne.DATE_DEB, ligne.DATE_FIN,
            ligne.HT, ligne.HS, ligne.NUIT, ligne.DIMANCHE, ligne.FERIES,
            ligne.RETARD, ligne.ABSENCE, ligne.VALIDATION_HS == true);

    private static async Task<T_HS_VALIDER> EnregistrerSemaineAsync(
        IRepository<T_HS_VALIDER> repo,
        WeeklyContext ctx,
        T_CARDPAIE card,
        T_HS_VALIDER? existante,
        OvertimeWeekResult semaine,
        DateTime from,
        DateTime to,
        CancellationToken ct)
    {
        if (existante != null)
        {
            existante.BDD_POINTAGE = ctx.PointeuseDb;
            existante.DATE_FIN = to;
            existante.HT = semaine.Ht;
            existante.HS = semaine.Hs;
            existante.NUIT = semaine.Nuit;
            existante.DIMANCHE = semaine.Dimanche;
            existante.FERIES = semaine.Ferie;
            existante.RETARD = semaine.Retard;
            existante.ABSENCE = semaine.Absence;
            existante.VALIDATION_HS = false;
            repo.Update(existante);
            return existante;
        }

        var nouvelle = new T_HS_VALIDER
        {
            BDD_SAGE = ctx.SageDb,
            BDD_POINTAGE = ctx.PointeuseDb,
            MATRICULES = card.SAGE_MATRICULE,
            DATE_DEB = from,
            DATE_FIN = to,
            HT = semaine.Ht,
            HS = semaine.Hs,
            NUIT = semaine.Nuit,
            DIMANCHE = semaine.Dimanche,
            FERIES = semaine.Ferie,
            RETARD = semaine.Retard,
            ABSENCE = semaine.Absence,
            VALIDATION_HS = false
        };
        await repo.AddAsync(nouvelle, ct);
        return nouvelle;
    }

    /// <summary>
    /// Détail du calcul des heures supplémentaires d'un salarié : jours issus des heures corrigées,
    /// répartition hebdomadaire EXO/IMPO et allocation enregistrée. Les jours passent par le même
    /// calculateur que la validation semaine, afin que l'écran de détail explique les valeurs payées.
    /// </summary>
    public async Task<OvertimeDetailDto> DetailAsync(string matricule, DateTime from, DateTime to, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        if (string.IsNullOrWhiteSpace(matricule))
            throw new InvalidOperationException("Matricule requis.");
        if (from == default || to == default)
            throw new InvalidOperationException("Période requise (du … au …).");
        var debut = from.Date;
        var fin = to.Date;
        if (fin < debut)
            throw new InvalidOperationException("La date de fin doit suivre la date de début.");

        var cle = MatriculeIdentity.Cle(matricule);
        var ctx = await ChargerContexteAsync(new PeriodRequest(null, null, debut, fin, null), avecExistantes: false, ct);
        var card = ctx.Cards.FirstOrDefault(c => MatriculeIdentity.Cle(c.SAGE_MATRICULE) == cle);
        var matriculeCarte = card?.SAGE_MATRICULE ?? matricule;
        var config = card == null ? new OvertimeWeekConfig(ctx.MultiPointage) : Configurer(ctx, card);

        var joursDetail = new List<OvertimeDetailDayDto>();
        if (card != null)
        {
            var lignesParJour = (ctx.Heures.GetValueOrDefault(cle) ?? new List<T_HEURECORRIGER>())
                .Where(h => h.DATE_POINTAGE_IN.HasValue)
                .GroupBy(h => h.DATE_POINTAGE_IN!.Value.Date)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var jour in ConstruireJours(ctx, card, debut, fin))
            {
                var resultat = OvertimeCalculator.CalculerJour(config, jour);
                var lignes = lignesParJour.GetValueOrDefault(jour.Jour) ?? new List<T_HEURECORRIGER>();
                var heuresSupJour = Math.Round(
                    OvertimeRules.HeuresSupJournalieres(
                        TimeSpan.FromHours((double)resultat.HeuresRetenues),
                        (double)config.Journee),
                    2);
                joursDetail.Add(new OvertimeDetailDayDto(
                    jour.Jour,
                    resultat.Entree,
                    resultat.Sortie,
                    resultat.HeuresRetenues,
                    heuresSupJour,
                    resultat.Nuit,
                    jour.Jour.DayOfWeek == DayOfWeek.Sunday,
                    jour.Ferie,
                    resultat.Retard > 0m ? TimeSpan.FromHours((double)resultat.Retard) : null,
                    lignes.Any(h => h.ABSENCE_AM == true),
                    lignes.Any(h => h.ABSENCE_PM == true),
                    resultat.Conge,
                    resultat.JourTravaille,
                    resultat.HeuresReelles));
            }
        }

        var semaines = (await _uow.Repository<T_HSSemaine>().ListAsync(h =>
                h.BDD_SAGE == _tenant.SageDb && h.Matricule == matriculeCarte, ct))
            .Where(h => h.PeriodeDebut.HasValue && h.PeriodeFin.HasValue
                        && h.PeriodeDebut!.Value.Date <= fin && h.PeriodeFin!.Value.Date >= debut)
            .OrderBy(h => h.Semaine)
            .Select(h => new OvertimeDetailWeekDto(h.Semaine, h.PeriodeDebut, h.PeriodeFin, h.HS ?? 0, h.HS1 ?? 0, h.HS2 ?? 0))
            .ToList();

        var exo = (await _uow.Repository<T_HSExoImp>().ListAsync(h =>
                h.BDD_SAGE == _tenant.SageDb && h.Matricule == matriculeCarte
                && h.PeriodeDebut == debut && h.PeriodeFin == fin, ct))
            .Select(h => new HsExoDto(h.OID, h.Matricule, h.NumSal, h.EXO130, h.EXO150, h.I130, h.I150,
                h.FERIE, h.NUIT, h.DIM, h.RETARD, h.ABSENCE))
            .FirstOrDefault();

        return new OvertimeDetailDto(matriculeCarte, debut, fin, joursDetail, semaines, exo);
    }

    // ------------------------------------------------------------------ contexte du calcul

    /// <summary>
    /// Contexte partagé par la semaine, l'EXO/IMPO et le détail. Tous les chargements sont groupés
    /// (une requête par table) : le calcul ne doit pas faire une requête par salarié.
    /// </summary>
    private sealed class WeeklyContext
    {
        public string SageDb { get; init; } = string.Empty;
        public string PointeuseDb { get; init; } = string.Empty;
        public bool MultiPointage { get; init; } = true;
        public List<T_CARDPAIE> Cards { get; init; } = new();
        public List<T_FERIE> Feries { get; init; } = new();
        public List<T_CATEGORIE> Categories { get; init; } = new();
        public List<T_CAT_SAL> Affectations { get; init; } = new();
        public List<T_TOLERANCE> Tolerances { get; init; } = new();
        public List<T_MAJORATION> Majorations { get; init; } = new();
        public Dictionary<string, List<LeaveDay>> Conges { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<T_HEURECORRIGER>> Heures { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<int, List<T_PLANNING_SAL>> Planning { get; init; } = new();
        public Dictionary<string, T_HS_VALIDER> Existantes { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, byte?> Civilites { get; init; } = new(StringComparer.Ordinal);
    }

    private async Task<WeeklyContext> ChargerContexteAsync(PeriodRequest request, bool avecExistantes, CancellationToken ct)
    {
        var from = request.From.Date;
        var to = request.To.Date;
        var sageDb = _tenant.SageDb;
        var pointeuseDb = _tenant.PointeuseDb;

        var cards = (await TargetCards(request, ct))
            .OrderBy(c => c.SAGE_MATRICULE, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var clock = (await _uow.Repository<T_CLOCK>().ListAsync(_ => true, ct)).FirstOrDefault();

        var heures = (await _uow.Repository<T_HEURECORRIGER>().ListAsync(
                h => h.BDD_SAGE == sageDb
                     && h.MATRICULE_SAGE != null
                     && h.DATE_POINTAGE_IN >= from
                     && h.DATE_POINTAGE_IN <= to, ct))
            .GroupBy(h => MatriculeIdentity.Cle(h.MATRICULE_SAGE))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var planning = (await _uow.Repository<T_PLANNING_SAL>().ListAsync(
                p => p.BDD_SAGE == sageDb && p.DateP >= from && p.DateP <= to, ct))
            .Where(p => string.IsNullOrWhiteSpace(p.BDD_POINTEUSE)
                        || string.Equals(p.BDD_POINTEUSE.Trim(), pointeuseDb.Trim(), StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => p.IDSAL)
            .ToDictionary(g => g.Key, g => g.ToList());

        var existantes = new Dictionary<string, T_HS_VALIDER>(StringComparer.Ordinal);
        if (avecExistantes)
        {
            existantes = (await _uow.Repository<T_HS_VALIDER>().ListAsync(
                    x => x.BDD_SAGE == sageDb && x.DATE_DEB == from, ct))
                .Where(x => !string.IsNullOrEmpty(x.MATRICULES))
                .GroupBy(x => MatriculeIdentity.Cle(x.MATRICULES))
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.ID).First(), StringComparer.Ordinal);
        }

        return new WeeklyContext
        {
            SageDb = sageDb,
            PointeuseDb = pointeuseDb,
            MultiPointage = clock?.MULTIPOINT ?? true,
            Cards = cards,
            Feries = await _uow.Repository<T_FERIE>().ListAsync(f => f.BDD_SAGE == sageDb, ct),
            Categories = await _uow.Repository<T_CATEGORIE>().ListAsync(c => c.BDD_SAGE == sageDb, ct),
            Affectations = await _uow.Repository<T_CAT_SAL>().ListAsync(a => a.BDD_SAGE == sageDb, ct),
            Tolerances = await _uow.Repository<T_TOLERANCE>().ListAsync(_ => true, ct),
            Majorations = await _uow.Repository<T_MAJORATION>().ListAsync(_ => true, ct),
            Conges = await _leaves.LoadAsync(ct),
            Heures = heures,
            Planning = planning,
            Existantes = existantes,
            Civilites = SemaineContientHuitMars(from, to)
                ? await ChargerCivilitesAsync(ct)
                : new Dictionary<string, byte?>(StringComparer.Ordinal)
        };
    }

    /// <summary>
    /// Paramétrage du salarié : catégorie (heures hebdo, pause), tolérances, priorité des majorations
    /// et civilité (règle du 8 mars). Les valeurs absentes retombent sur les défauts de la synthèse.
    /// </summary>
    private static OvertimeWeekConfig Configurer(WeeklyContext ctx, T_CARDPAIE card)
    {
        var affectation = ctx.Affectations.FirstOrDefault(a => a.MATRICULE_SAGE == card.ID);
        var categorie = affectation == null
            ? null
            : ctx.Categories.FirstOrDefault(c => c.IDCATEGORIE == affectation.ID_CATEG);
        var tolerance = categorie == null
            ? null
            : ctx.Tolerances.FirstOrDefault(t => t.IDCATEGORIE == categorie.IDCATEGORIE && !t.TYPES_TOL);
        ctx.Civilites.TryGetValue(MatriculeIdentity.Cle(card.SAGE_MATRICULE), out var civilite);

        return new OvertimeWeekConfig(
            MultiPointage: ctx.MultiPointage,
            HeuresSemaine: categorie?.HEURESEMAINE ?? OvertimeRules.DefaultWeeklyHours,
            PauseCategorie: categorie?.PAUSE,
            ToleranceEntreeMinutes: tolerance?.TOLERANCE ?? 0,
            PasSortieMinutes: tolerance?.SORTIE,
            CotationNuit: Cotation(ctx.Majorations, "NUIT"),
            CotationDimanche: Cotation(ctx.Majorations, "DIMANCHE"),
            CotationFerie: Cotation(ctx.Majorations, "FERIE"),
            EstFemme: civilite is > 0);
    }

    private static decimal Cotation(IReadOnlyList<T_MAJORATION> majorations, string type)
        => majorations
            .Where(m => string.Equals(m.Mojoration?.Trim(), type, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Cotation ?? 0m)
            .DefaultIfEmpty(0m)
            .Max();

    /// <summary>Journées du calcul : paires, planning (reconstruit s'il manque), férié, congé, retard saisi.</summary>
    private static List<OvertimeDayInput> ConstruireJours(WeeklyContext ctx, T_CARDPAIE card, DateTime from, DateTime to)
    {
        var cle = MatriculeIdentity.Cle(card.SAGE_MATRICULE);
        var heures = ctx.Heures.GetValueOrDefault(cle) ?? new List<T_HEURECORRIGER>();
        var planning = ctx.Planning.GetValueOrDefault(card.ID) ?? new List<T_PLANNING_SAL>();
        var conges = ctx.Conges.GetValueOrDefault(cle) ?? new List<LeaveDay>();
        var jours = new List<OvertimeDayInput>();

        for (var jour = from.Date; jour <= to.Date; jour = jour.AddDays(1))
        {
            var lignes = heures
                .Where(h => h.DATE_POINTAGE_IN.HasValue && h.DATE_POINTAGE_IN.Value.Date == jour)
                .OrderBy(h => h.HENTREE ?? TimeSpan.Zero)
                .ToList();
            var paires = lignes
                .Where(h => h.HENTREE.HasValue && h.HSORTIE.HasValue)
                .Select(h => new PunchPair(
                    h.DATE_POINTAGE_IN!.Value.Date,
                    h.HENTREE!.Value,
                    (h.DATE_POINTAGE_OUT ?? h.DATE_POINTAGE_IN)!.Value.Date,
                    h.HSORTIE!.Value))
                .ToList();

            var plan = planning
                .Where(p => p.DateP.HasValue && p.DateP.Value.Date == jour)
                .OrderBy(p => p.ID)
                .FirstOrDefault();
            var off = plan?.OffP == true;
            // Les heures de planning « vides » du desktop portent une date sentinelle : elles valent NULL.
            var sentinelle = new DateTime(2000, 1, 1);
            var haPlanifiee = plan?.HA is DateTime haBrute && haBrute.Date > sentinelle ? haBrute.TimeOfDay : (TimeSpan?)null;
            var hdPlanifiee = plan?.HD is DateTime hdBrute && hdBrute.Date > sentinelle ? hdBrute.TimeOfDay : (TimeSpan?)null;
            TimeSpan? ha = off ? null : haPlanifiee;
            TimeSpan? hd = off ? null : hdPlanifiee;

            // Planning manquant : le desktop le reconstruit avec les heures réelles (entrée et sortie
            // non corrigées), ce qui neutralise le seuil journalier du mode 2 pointages.
            if (!off && paires.Count > 0)
            {
                ha ??= paires[0].EntreeHeure;
                hd ??= paires[^1].SortieHeure;
            }

            jours.Add(new OvertimeDayInput(
                jour,
                paires,
                ha,
                hd,
                off ? null : plan?.Pause,
                off,
                ctx.Feries.Any(f => f.DATE.HasValue && f.DATE.Value.Date == jour),
                lignes.Count > 0,
                LeaveRules.Valeur(conges, jour),
                lignes.FirstOrDefault(h => h.RETARD.HasValue)?.RETARD));
        }
        return jours;
    }

    /// <summary>La civilité n'est chargée que si la période contient un 8 mars (évite un appel SAGE systématique).</summary>
    private static bool SemaineContientHuitMars(DateTime from, DateTime to)
    {
        for (var jour = from.Date; jour <= to.Date; jour = jour.AddDays(1))
        {
            if (jour.Day == 8 && jour.Month == 3)
                return true;
        }
        return false;
    }

    private async Task<Dictionary<string, byte?>> ChargerCivilitesAsync(CancellationToken ct)
    {
        var civilites = new Dictionary<string, byte?>(StringComparer.Ordinal);
        try
        {
            var sageRow = await _tenant.GetSageRowAsync(ct);
            foreach (var employe in await _externalSource.GetEmployeesAsync(sageRow, ct))
            {
                var cle = MatriculeIdentity.Cle(employe.Matricule);
                if (cle.Length > 0)
                    civilites[cle] = employe.Civilite;
            }
        }
        catch
        {
            // SAGE indisponible : sans civilité, la règle du 8 mars n'est pas appliquée.
        }
        return civilites;
    }

    private async Task<Dictionary<string, int>> ChargerNumSalAsync(CancellationToken ct)
    {
        var resultat = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            var sageRow = await _tenant.GetSageRowAsync(ct);
            foreach (var employe in await _externalSource.GetEmployeesAsync(sageRow, ct))
            {
                var cle = MatriculeIdentity.Cle(employe.Matricule);
                if (cle.Length > 0)
                    resultat[cle] = (int)employe.EmployeeId;
            }
        }
        catch
        {
            // SAGE optionnel pour l'aperçu : le numéro de salarié restera nul.
        }
        return resultat;
    }

    private static decimal Arrondir(decimal valeur) => Math.Round(valeur, 2);

    private static void EnsureMondayToSunday(PeriodRequest request)
    {
        if (!OvertimeRules.IsMondayToSundayWeek(request.From, request.To))
            throw new InvalidOperationException("La période doit être un lundi–dimanche (7 jours).");
    }

    private static void EnsureMaxOneMonth(PeriodRequest request)
    {
        if (request.To.Date < request.From.Date)
            throw new InvalidOperationException("La date de début doit être antérieure ou égale à la date de fin.");
        if (!OvertimeRules.IsMaxOneMonth(request.From, request.To))
            throw new InvalidOperationException("La période sélectionnée ne doit pas dépasser un mois.");
    }

    /// <summary>
    /// Cartes du périmètre. Le filtre pointeuse est tolérant (lignes héritées sans base renseignée)
    /// et identique à celui du pré-traitement des pointages.
    /// </summary>
    private async Task<List<T_CARDPAIE>> TargetCards(PeriodRequest request, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        var pointeuseDb = _tenant.PointeuseDb;
        var cards = await _uow.Repository<T_CARDPAIE>().ListAsync(c =>
            c.SAGE_BDD == sageDb
            && (string.IsNullOrEmpty(request.Branche) || c.BRANCHE == request.Branche)
            && (string.IsNullOrEmpty(request.Departement) || c.DEPARTEMENT == request.Departement)
            && (string.IsNullOrEmpty(request.MatriculeFrom) || string.Compare(c.SAGE_MATRICULE, request.MatriculeFrom) >= 0)
            && (string.IsNullOrEmpty(request.MatriculeTo) || string.Compare(c.SAGE_MATRICULE, request.MatriculeTo) <= 0), ct);
        return cards
            .Where(c => string.IsNullOrWhiteSpace(c.POINTEUSE_BDD)
                        || string.Equals(c.POINTEUSE_BDD.Trim(), pointeuseDb.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(c => !string.IsNullOrWhiteSpace(c.SAGE_MATRICULE))
            .GroupBy(c => MatriculeIdentity.Cle(c.SAGE_MATRICULE))
            .Select(g => g.OrderBy(c => c.ID).First())
            .ToList();
    }
}
