using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

/// <summary>
/// Calcul et validation des heures supplémentaires (portage de <c>Frm_Validations</c>,
/// <c>Frm_CalculHS</c> et des méthodes <c>setHSSemaine</c> / <c>setHSEXO130EXO150I130I150</c>
/// du desktop). Les règles métier pures sont dans <see cref="OvertimeCalculator"/> ; ce service
/// charge le contexte, applique le périmètre puis persiste <c>T_HS_VALIDER</c>, <c>T_HSSemaine</c>
/// et <c>T_HSExoImp</c>.
/// </summary>
public class OvertimeService : IOvertimeService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IExternalSourceService _externalSource;
    private readonly IPayrollWriterResolver _payrollWriters;
    private readonly IHistoryArchiveService _history;
    private readonly ISageLeaveCalendar _leaves;
    private readonly ICurrentUser? _user;

    public OvertimeService(IUnitOfWork uow, ITenantConnectionService tenant, IExternalSourceService externalSource, IPayrollWriterResolver payrollWriters, IHistoryArchiveService history, ISageLeaveCalendar leaves)
        : this(uow, tenant, externalSource, payrollWriters, history, leaves, null)
    {
    }

    public OvertimeService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IExternalSourceService externalSource,
        IPayrollWriterResolver payrollWriters,
        IHistoryArchiveService history,
        ISageLeaveCalendar leaves,
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
        var filtered = rows
            .Where(row => SearchText.Matches(request.Search, row.Matricule))
            .ToList();
        return PagedLists.Page(filtered, page, pageSize);
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
        var hsRepo = _uow.Repository<HsValidee>();
        var corrRepo = _uow.Repository<HeureCorrigee>();

        // Un recalcul est nécessaire pour obtenir les valeurs à valider (les semaines déjà validées
        // sont exclues du calcul et donc absentes de `rows`).
        if (validate)
            await ComputeWeekAsync(request, persistNonValidated: true, ct);
        else
            await RefuserSiSemaineCalculeeAsync(sageDb, from, selected, ct);

        var lignes = (await hsRepo.ListAsync(x => x.BddSage == sageDb && x.DateDeb == from, ct))
            .Where(x => !string.IsNullOrEmpty(x.Matricules) && selected.Contains(MatriculeIdentity.Cle(x.Matricules)))
            .GroupBy(x => MatriculeIdentity.Cle(x.Matricules))
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Id).First(), StringComparer.Ordinal);

        var corrections = await corrRepo.ListAsync(h => h.BddSage == sageDb
            && h.DatePointageIn >= from && h.DatePointageIn <= to, ct);
        var correctionsParCle = corrections
            .Where(h => !string.IsNullOrEmpty(h.MatriculeSage))
            .GroupBy(h => MatriculeIdentity.Cle(h.MatriculeSage))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var (cle, ligne) in lignes)
        {
            if (validate && ligne.ValidationHs == true)
                continue;
            if (!validate && ligne.ValidationHs != true)
                continue;

            ligne.ValidationHs = validate;
            ligne.DateFin = to;
            hsRepo.Update(ligne);

            if (correctionsParCle.TryGetValue(cle, out var heures))
            {
                foreach (var heure in heures)
                {
                    heure.ValiderHs = validate;
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
        var cards = await TargetCardsAsync(request, ct);
        var targetMatricules = cards.Select(c => c.SageMatricule).Where(m => !string.IsNullOrEmpty(m)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var list = await _uow.Repository<HsExoImp>().ListReadOnlyAsync(h =>
            h.BddSage == _tenant.SageDb
            && h.PeriodeDebut <= request.To.Date
            && h.PeriodeFin >= request.From.Date
            && (string.IsNullOrEmpty(request.MatriculeFrom) || string.Compare(h.Matricule, request.MatriculeFrom) >= 0)
            && (string.IsNullOrEmpty(request.MatriculeTo) || string.Compare(h.Matricule, request.MatriculeTo) <= 0), ct);

        if (!string.IsNullOrEmpty(request.Departement))
        {
            list = list.Where(h => h.Matricule != null && targetMatricules.Contains(h.Matricule)).ToList();
        }

        return list
            .OrderBy(h => h.Matricule)
            .Select(h => new HsExoDto(h.Oid, h.Matricule, h.NumSal, h.Exo130, h.Exo150, h.I130, h.I150, h.Ferie, h.Nuit, h.Dim, h.Retard, h.Absence))
            .ToList();
    }

    /// <summary>
    /// Liste paginée des cumuls EXO/IMPO. Le filtre est traduit en SQL ; le filtre
    /// département est résolu via les cartes cibles (même sémantique que
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
        var repo = _uow.Repository<HsExoImp>();

        static HsExoDto Map(HsExoImp h) => new(
            h.Oid, h.Matricule, h.NumSal, h.Exo130, h.Exo150, h.I130, h.I150,
            h.Ferie, h.Nuit, h.Dim, h.Retard, h.Absence);

        if (string.IsNullOrEmpty(request.Departement))
        {
            return await PagedLists.PageAsync(
                repo,
                h => h.BddSage == sageDb
                     && h.PeriodeDebut <= to
                     && h.PeriodeFin >= from
                     && (string.IsNullOrEmpty(matriculeFrom) || string.Compare(h.Matricule, matriculeFrom) >= 0)
                     && (string.IsNullOrEmpty(matriculeTo) || string.Compare(h.Matricule, matriculeTo) <= 0),
                q => q.OrderBy(h => h.Matricule).ThenBy(h => h.Oid),
                page,
                pageSize,
                Map,
                request.Search,
                new System.Linq.Expressions.Expression<Func<HsExoImp, string?>>[] { h => h.Matricule },
                ct);
        }

        var cards = await TargetCardsAsync(request, ct);
        var targetMatricules = cards
            .Select(c => c.SageMatricule)
            .Where(m => !string.IsNullOrEmpty(m))
            .Select(m => m!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return await PagedLists.PageAsync(
            repo,
            h => h.BddSage == sageDb
                 && h.PeriodeDebut <= to
                 && h.PeriodeFin >= from
                 && (string.IsNullOrEmpty(matriculeFrom) || string.Compare(h.Matricule, matriculeFrom) >= 0)
                 && (string.IsNullOrEmpty(matriculeTo) || string.Compare(h.Matricule, matriculeTo) <= 0)
                 && targetMatricules.Contains(h.Matricule!),
            q => q.OrderBy(h => h.Matricule).ThenBy(h => h.Oid),
            page,
            pageSize,
            Map,
            request.Search,
            new System.Linq.Expressions.Expression<Func<HsExoImp, string?>>[] { h => h.Matricule },
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

        var cards = (await TargetCardsAsync(request, ct))
            .OrderBy(c => c.SageMatricule, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var targetCles = cards
            .Select(c => MatriculeIdentity.Cle(c.SageMatricule))
            .Where(cle => cle.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var numSalByMat = await ChargerNumSalAsync(ct);

        // Une semaine validée appartient à UNE seule période : celle qui contient son dimanche
        // (le desktop passait par T_SemainePeriode). Une période démarrant un mercredi inclut donc
        // sa première semaine, mais la semaine qui déborde sur la période suivante n'est payée
        // qu'avec celle-ci : sinon elle serait comptée dans deux T_HSExoImp consécutifs.
        var (debutSemaineMin, debutSemaineMax) = BornesSemainesDeLaPeriode(from, to);
        var validees = (await _uow.Repository<HsValidee>().ListAsync(
                h => h.BddSage == sageDb
                     && h.ValidationHs == true
                     && h.DateDeb != null
                     && h.DateDeb >= debutSemaineMin
                     && h.DateDeb <= debutSemaineMax, ct))
            .Where(h => !string.IsNullOrEmpty(h.Matricules) && targetCles.Contains(MatriculeIdentity.Cle(h.Matricules)))
            .GroupBy(h => MatriculeIdentity.Cle(h.Matricules), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.DateDeb).ToList(), StringComparer.Ordinal);

        // Absence de la période : calculée sur le planning, indépendamment de la validation.
        var ctx = await ChargerContexteAsync(request, avecExistantes: false, ct);
        var absences = cards.ToDictionary(
            c => MatriculeIdentity.Cle(c.SageMatricule),
            c => Arrondir(ConstruireJours(ctx, c, from, to).Sum(j => OvertimeCalculator.CalculerJour(Configurer(ctx, c), j).Absence)),
            StringComparer.Ordinal);

        var semaineRepo = _uow.Repository<HsSemaine>();
        var exoRepo = _uow.Repository<HsExoImp>();
        var anciennesSemaines = (await semaineRepo.ListAsync(
                h => h.BddSage == sageDb && h.PeriodeDebut == from && h.PeriodeFin == to, ct))
            .Where(h => h.Matricule != null && targetCles.Contains(MatriculeIdentity.Cle(h.Matricule)))
            .ToList();
        var anciennesExo = (await exoRepo.ListAsync(
                h => h.BddSage == sageDb && h.PeriodeDebut == from && h.PeriodeFin == to, ct))
            .Where(h => h.Matricule != null && targetCles.Contains(MatriculeIdentity.Cle(h.Matricule)))
            .ToList();

        var nouvellesSemaines = new List<HsSemaine>();
        var nouvellesExo = new List<HsExoImp>();

        foreach (var card in cards)
        {
            var cle = MatriculeIdentity.Cle(card.SageMatricule);
            var semaines = validees.GetValueOrDefault(cle) ?? new List<HsValidee>();
            var splits = semaines.Select(w => OvertimeRules.SplitWeekly(w.Hs ?? 0)).ToList();
            var numSal = numSalByMat.TryGetValue(cle, out var n) ? n : (int?)null;

            // PeriodeDebut/PeriodeFin portent la PÉRIODE du calcul (clé de la purge, de la liste et de
            // la synchro paie, et compatibilité desktop) ; les bornes lundi → dimanche de chaque semaine
            // sont déduites à la lecture du détail (voir ConstruireSemainesDetail).
            var rang = 1;
            foreach (var split in splits)
            {
                nouvellesSemaines.Add(new HsSemaine
                {
                    Matricule = card.SageMatricule,
                    SalNum = numSal,
                    TotalHeure = split.Hs,
                    Hs = split.Hs,
                    Hs1 = split.Hs1,
                    Hs2 = split.Hs2,
                    PeriodeDebut = from,
                    PeriodeFin = to,
                    Semaine = rang++,
                    BddSage = sageDb,
                    BddPointeuse = _tenant.PointeuseDb
                });
            }

            var exo = OvertimeRules.AllocateExempt(splits);
            var absence = absences.GetValueOrDefault(cle);
            if (semaines.Count == 0 && absence == 0m)
                continue;

            nouvellesExo.Add(new HsExoImp
            {
                Matricule = card.SageMatricule,
                NumSal = numSal,
                Exo130 = exo.Exo130,
                Exo150 = exo.Exo150,
                I130 = exo.Impo130,
                I150 = exo.Impo150,
                PeriodeDebut = from,
                PeriodeFin = to,
                Nuit = Arrondir(semaines.Sum(x => x.Nuit ?? 0m)),
                Dim = Arrondir(semaines.Sum(x => x.Dimanche ?? 0m)),
                Ferie = Arrondir(semaines.Sum(x => x.Feries ?? 0m)),
                Retard = Arrondir(semaines.Sum(x => x.Retard ?? 0m)),
                Absence = absence,
                BddSage = sageDb,
                BddPointeuse = _tenant.PointeuseDb
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
            .Select(e => new HsExoDto(e.Oid, e.Matricule, e.NumSal, e.Exo130, e.Exo150, e.I130, e.I150,
                e.Ferie, e.Nuit, e.Dim, e.Retard, e.Absence))
            .ToList();
    }

    public async Task PurgeHsAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        EnsureMaxOneMonth(request);
        var sem = await _uow.Repository<HsSemaine>().ListAsync(h =>
            h.BddSage == _tenant.SageDb && h.PeriodeDebut == request.From.Date && h.PeriodeFin == request.To.Date
            && (string.IsNullOrEmpty(request.MatriculeFrom) || string.Compare(h.Matricule, request.MatriculeFrom) >= 0)
            && (string.IsNullOrEmpty(request.MatriculeTo) || string.Compare(h.Matricule, request.MatriculeTo) <= 0), ct);
        var exo = await _uow.Repository<HsExoImp>().ListAsync(h =>
            h.BddSage == _tenant.SageDb && h.PeriodeDebut == request.From.Date && h.PeriodeFin == request.To.Date
            && (string.IsNullOrEmpty(request.MatriculeFrom) || string.Compare(h.Matricule, request.MatriculeFrom) >= 0)
            && (string.IsNullOrEmpty(request.MatriculeTo) || string.Compare(h.Matricule, request.MatriculeTo) <= 0), ct);

        if (!string.IsNullOrEmpty(request.Departement))
        {
            var cards = await TargetCardsAsync(request, ct);
            var targetMatricules = cards.Select(c => c.SageMatricule).Where(m => !string.IsNullOrEmpty(m)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            sem = sem.Where(h => h.Matricule != null && targetMatricules.Contains(h.Matricule)).ToList();
            exo = exo.Where(h => h.Matricule != null && targetMatricules.Contains(h.Matricule)).ToList();
        }

        _uow.Repository<HsSemaine>().RemoveRange(sem);
        await _history.ArchiveHsAsync(exo, ct);
        _uow.Repository<HsExoImp>().RemoveRange(exo);
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

        var repo = _uow.Repository<HsValidee>();
        var lignes = (await repo.ListAsync(
                v => v.BddSage == sageDb && v.DateDeb == from && v.DateFin == to, ct))
            .Where(v => MatriculeRange.Contains(v.Matricules, request))
            .ToList();
        if (lignes.Count == 0)
            return new PurgeValidationResultDto(0, 0, "Aucune validation à purger sur la période.");

        await RefuserSiSemaineCalculeeAsync(
            sageDb,
            from,
            lignes.Select(v => MatriculeIdentity.Cle(v.Matricules)).Where(cle => cle.Length > 0).ToHashSet(StringComparer.Ordinal),
            ct);

        var corrRepo = _uow.Repository<HeureCorrigee>();
        var corrections = await corrRepo.ListAsync(h => h.BddSage == sageDb
            && h.DatePointageIn >= from && h.DatePointageIn <= to, ct);
        var correctionsParCle = corrections
            .Where(h => !string.IsNullOrEmpty(h.MatriculeSage))
            .GroupBy(h => MatriculeIdentity.Cle(h.MatriculeSage))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var deverrouillees = 0;
        foreach (var ligne in lignes)
        {
            if (!correctionsParCle.TryGetValue(MatriculeIdentity.Cle(ligne.Matricules), out var heures))
                continue;
            foreach (var heure in heures)
            {
                if (heure.ValiderHs != true)
                    continue;
                heure.ValiderHs = false;
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
        if (!string.Equals(sageRow.TypeBase, "STANDARD", StringComparison.OrdinalIgnoreCase))
            throw new DomainException(
                "La synchronisation SAGE est disponible uniquement pour une base STANDARD. Utilisez la synchronisation de paie pour une base AUTRE.");
        await SyncPayrollAsync(request, ct);
    }

    // Ecriture de paie unifiee : le resolveur choisit le writer STANDARD (T_CUMSAL) ou AUTRE (T_PAIE_ECRITURE).
    public async Task<PayrollWriteResultDto> SyncPayrollAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        EnsureMaxOneMonth(request);
        var sageRow = await _tenant.GetSageRowAsync(ct);
        var rows = await _uow.Repository<HsExoImp>().ListReadOnlyAsync(h =>
            h.BddSage == _tenant.SageDb && h.PeriodeDebut == request.From.Date && h.PeriodeFin == request.To.Date, ct);

        if (!string.IsNullOrEmpty(request.Departement))
        {
            var cards = await TargetCardsAsync(request, ct);
            var targetMatricules = cards.Select(c => c.SageMatricule).Where(m => !string.IsNullOrEmpty(m)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            rows = rows.Where(h => h.Matricule != null && targetMatricules.Contains(h.Matricule)).ToList();
        }

        var dtos = rows
            .Select(h => new HsExoDto(h.Oid, h.Matricule, h.NumSal, h.Exo130, h.Exo150, h.I130, h.I150,
                h.Ferie, h.Nuit, h.Dim, h.Retard, h.Absence))
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
        var hsRepo = _uow.Repository<HsValidee>();
        var resultat = new List<WeeklyHsDto>(ctx.Cards.Count);

        foreach (var card in ctx.Cards)
        {
            var cle = MatriculeIdentity.Cle(card.SageMatricule);
            ctx.Existantes.TryGetValue(cle, out var existante);
            if (existante?.ValidationHs == true)
            {
                resultat.Add(DepuisEntite(existante));
                continue;
            }

            var config = Configurer(ctx, card);
            var semaine = OvertimeCalculator.CalculerSemaine(config, ConstruireJours(ctx, card, from, to));
            if (persistNonValidated)
                existante = await EnregistrerSemaineAsync(hsRepo, ctx, card, existante, semaine, from, to, ct);

            resultat.Add(new WeeklyHsDto(
                existante?.Id ?? 0, card.SageMatricule, from, to,
                semaine.Ht, semaine.Hs, semaine.Nuit, semaine.Dimanche, semaine.Ferie,
                semaine.Retard, semaine.Absence, Validated: false));
        }

        if (persistNonValidated)
            await _uow.SaveChangesAsync(ct);
        return resultat;
    }

    private static WeeklyHsDto DepuisEntite(HsValidee ligne)
        => new(ligne.Id, ligne.Matricules, ligne.DateDeb, ligne.DateFin,
            ligne.Ht, ligne.Hs, ligne.Nuit, ligne.Dimanche, ligne.Feries,
            ligne.Retard, ligne.Absence, ligne.ValidationHs == true);

    private static async Task<HsValidee> EnregistrerSemaineAsync(
        IRepository<HsValidee> repo,
        WeeklyContext ctx,
        CardPaie card,
        HsValidee? existante,
        OvertimeWeekResult semaine,
        DateTime from,
        DateTime to,
        CancellationToken ct)
    {
        if (existante != null)
        {
            existante.BddPointage = ctx.PointeuseDb;
            existante.DateFin = to;
            existante.Ht = semaine.Ht;
            existante.Hs = semaine.Hs;
            existante.Nuit = semaine.Nuit;
            existante.Dimanche = semaine.Dimanche;
            existante.Feries = semaine.Ferie;
            existante.Retard = semaine.Retard;
            existante.Absence = semaine.Absence;
            existante.ValidationHs = false;
            repo.Update(existante);
            return existante;
        }

        var nouvelle = new HsValidee
        {
            BddSage = ctx.SageDb,
            BddPointage = ctx.PointeuseDb,
            Matricules = card.SageMatricule,
            DateDeb = from,
            DateFin = to,
            Ht = semaine.Ht,
            Hs = semaine.Hs,
            Nuit = semaine.Nuit,
            Dimanche = semaine.Dimanche,
            Feries = semaine.Ferie,
            Retard = semaine.Retard,
            Absence = semaine.Absence,
            ValidationHs = false
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
            throw new DomainException("Matricule requis.");
        if (from == default || to == default)
            throw new DomainException("Période requise (du … au …).");
        var debut = from.Date;
        var fin = to.Date;
        if (fin < debut)
            throw new DomainException("La date de fin doit suivre la date de début.");

        var cle = MatriculeIdentity.Cle(matricule);
        var ctx = await ChargerContexteAsync(new PeriodRequest(null, null, debut, fin), avecExistantes: false, ct);
        var card = ctx.Cards.FirstOrDefault(c => MatriculeIdentity.Cle(c.SageMatricule) == cle);
        var matriculeCarte = card?.SageMatricule ?? matricule;
        var config = card == null ? new OvertimeWeekConfig(ctx.MultiPointage) : Configurer(ctx, card);

        var joursDetail = new List<OvertimeDetailDayDto>();
        if (card != null)
        {
            var lignesParJour = (ctx.Heures.GetValueOrDefault(cle) ?? new List<HeureCorrigee>())
                .Where(h => h.DatePointageIn.HasValue)
                .GroupBy(h => h.DatePointageIn!.Value.Date)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var jour in ConstruireJours(ctx, card, debut, fin))
            {
                var resultat = OvertimeCalculator.CalculerJour(config, jour);
                var lignes = lignesParJour.GetValueOrDefault(jour.Jour) ?? new List<HeureCorrigee>();
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
                    lignes.Any(h => h.AbsenceAm == true),
                    lignes.Any(h => h.AbsencePm == true),
                    resultat.Conge,
                    resultat.JourTravaille,
                    resultat.HeuresReelles));
            }
        }

        // T_HSSemaine.PeriodeDebut/PeriodeFin portent la PÉRIODE du calcul (clé de la purge, de la
        // liste et de la synchro paie, et sémantique du desktop) : les bornes de chaque semaine sont
        // déduites des semaines validées, sinon les colonnes Début/Fin afficheraient la période.
        var lignesSemaine = (await _uow.Repository<HsSemaine>().ListAsync(h =>
                h.BddSage == _tenant.SageDb && h.Matricule == matriculeCarte, ct))
            .Where(h => h.PeriodeDebut.HasValue && h.PeriodeFin.HasValue
                        && h.PeriodeDebut!.Value.Date <= fin && h.PeriodeFin!.Value.Date >= debut)
            .OrderBy(h => h.PeriodeDebut)
            .ThenBy(h => h.Semaine)
            .ThenBy(h => h.Oid)
            .ToList();
        var semainesValidees = await ChargerSemainesValideesAsync(matriculeCarte, lignesSemaine, ct);
        var semaines = ConstruireSemainesDetail(lignesSemaine, semainesValidees);

        var exo = (await _uow.Repository<HsExoImp>().ListAsync(h =>
                h.BddSage == _tenant.SageDb && h.Matricule == matriculeCarte
                && h.PeriodeDebut == debut && h.PeriodeFin == fin, ct))
            .Select(h => new HsExoDto(h.Oid, h.Matricule, h.NumSal, h.Exo130, h.Exo150, h.I130, h.I150,
                h.Ferie, h.Nuit, h.Dim, h.Retard, h.Absence))
            .FirstOrDefault();

        return new OvertimeDetailDto(matriculeCarte, debut, fin, joursDetail, semaines, exo);
    }

    /// <summary>
    /// Semaines validées du salarié nécessaires pour retrouver les bornes réelles des semaines de
    /// <c>T_HSSemaine</c>. La borne basse reprend celle du calcul EXO/IMPO (période − 6 jours) pour
    /// inclure une première semaine qui commence avant le mois sélectionné.
    /// </summary>
    private async Task<List<HsValidee>> ChargerSemainesValideesAsync(
        string matricule, IReadOnlyList<HsSemaine> lignes, CancellationToken ct)
    {
        var debuts = lignes
            .Where(h => h.PeriodeDebut.HasValue)
            .Select(h => h.PeriodeDebut!.Value.Date)
            .ToList();
        var fins = lignes
            .Where(h => h.PeriodeFin.HasValue)
            .Select(h => h.PeriodeFin!.Value.Date)
            .ToList();
        if (debuts.Count == 0 || fins.Count == 0)
            return new List<HsValidee>();

        var (borneBasse, borneHaute) = BornesSemainesDeLaPeriode(debuts.Min(), fins.Max());
        var sageDb = _tenant.SageDb;

        var semaines = await _uow.Repository<HsValidee>().ListAsync(v =>
            v.BddSage == sageDb
            && v.ValidationHs == true
            && v.DateDeb != null
            && v.DateDeb >= borneBasse
            && v.DateDeb <= borneHaute, ct);

        return semaines
            .Where(v => v.DateDeb.HasValue && MatriculeIdentity.MemeSalarie(v.Matricules, matricule))
            .GroupBy(v => v.DateDeb!.Value.Date)
            .Select(g => g.OrderBy(v => v.Id).First())
            .OrderBy(v => v.DateDeb)
            .ToList();
    }

    /// <summary>
    /// Bornes de chaque semaine affichée dans le détail. <c>T_HSSemaine.Semaine</c> est le rang de la
    /// semaine parmi les semaines validées de la période : on le résout dans ces semaines
    /// (<c>T_HS_VALIDER.DATE_DEB/DATE_FIN</c>, lundi → dimanche). Si les données ne concordent plus
    /// (validation ajoutée ou purgée après le calcul), on retombe sur la période stockée plutôt que
    /// d'afficher des dates décalées.
    /// </summary>
    private static List<OvertimeDetailWeekDto> ConstruireSemainesDetail(
        IReadOnlyList<HsSemaine> lignes, IReadOnlyList<HsValidee> semainesValidees)
    {
        var resultat = new List<OvertimeDetailWeekDto>(lignes.Count);

        foreach (var groupe in lignes.GroupBy(h => (Debut: h.PeriodeDebut?.Date, Fin: h.PeriodeFin?.Date)))
        {
            var lignesGroupe = groupe.ToList();
            var candidates = groupe.Key.Debut.HasValue && groupe.Key.Fin.HasValue
                ? semainesValidees
                    .Where(v => v.DateDeb.HasValue
                                && SemaineRattachee(v.DateDeb.Value, groupe.Key.Debut, groupe.Key.Fin))
                    .ToList()
                : new List<HsValidee>();

            var coherent = candidates.Count == lignesGroupe.Count
                           && lignesGroupe.All(h => h.Semaine is int rang && rang >= 1 && rang <= candidates.Count);

            foreach (var ligne in lignesGroupe)
            {
                DateTime? debutSemaine = groupe.Key.Debut;
                DateTime? finSemaine = groupe.Key.Fin;

                if (coherent && ligne.Semaine is int rang)
                {
                    var semaine = candidates[rang - 1];
                    debutSemaine = semaine.DateDeb!.Value.Date;
                    finSemaine = (semaine.DateFin ?? semaine.DateDeb!.Value.AddDays(6)).Date;
                }

                resultat.Add(new OvertimeDetailWeekDto(
                    ligne.Semaine, debutSemaine, finSemaine, ligne.Hs ?? 0, ligne.Hs1 ?? 0, ligne.Hs2 ?? 0));
            }
        }

        return resultat;
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
        public List<CardPaie> Cards { get; init; } = new();
        public List<JourFerie> Feries { get; init; } = new();
        public List<Categorie> Categories { get; init; } = new();
        public List<CategorieSalarie> Affectations { get; init; } = new();
        public List<ToleranceRetard> Tolerances { get; init; } = new();
        public List<Majoration> Majorations { get; init; } = new();
        public Dictionary<string, List<LeaveDay>> Conges { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<HeureCorrigee>> Heures { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<int, List<PlanningSalarie>> Planning { get; init; } = new();
        public Dictionary<string, HsValidee> Existantes { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, byte?> Civilites { get; init; } = new(StringComparer.Ordinal);
    }

    private async Task<WeeklyContext> ChargerContexteAsync(PeriodRequest request, bool avecExistantes, CancellationToken ct)
    {
        var from = request.From.Date;
        var to = request.To.Date;
        var sageDb = _tenant.SageDb;
        var pointeuseDb = _tenant.PointeuseDb;

        var cards = (await TargetCardsAsync(request, ct))
            .OrderBy(c => c.SageMatricule, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var clock = (await _uow.Repository<ParametrePointeuse>().ListAsync(_ => true, ct)).FirstOrDefault();

        var heures = (await _uow.Repository<HeureCorrigee>().ListAsync(
                h => h.BddSage == sageDb
                     && h.MatriculeSage != null
                     && h.DatePointageIn >= from
                     && h.DatePointageIn <= to, ct))
            .GroupBy(h => MatriculeIdentity.Cle(h.MatriculeSage))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var planning = (await _uow.Repository<PlanningSalarie>().ListAsync(
                p => p.BddSage == sageDb && p.DateP >= from && p.DateP <= to, ct))
            .Where(p => OvertimeDayRules.EstDeLaPointeuse(p, pointeuseDb))
            .GroupBy(p => p.IdSal)
            .ToDictionary(g => g.Key, g => g.ToList());

        var existantes = new Dictionary<string, HsValidee>(StringComparer.Ordinal);
        if (avecExistantes)
        {
            existantes = (await _uow.Repository<HsValidee>().ListAsync(
                    x => x.BddSage == sageDb && x.DateDeb == from, ct))
                .Where(x => !string.IsNullOrEmpty(x.Matricules))
                .GroupBy(x => MatriculeIdentity.Cle(x.Matricules))
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Id).First(), StringComparer.Ordinal);
        }

        return new WeeklyContext
        {
            SageDb = sageDb,
            PointeuseDb = pointeuseDb,
            MultiPointage = clock?.MultiPoint ?? true,
            Cards = cards,
            Feries = await _uow.Repository<JourFerie>().ListAsync(f => f.BddSage == sageDb, ct),
            Categories = await _uow.Repository<Categorie>().ListAsync(c => c.BddSage == sageDb, ct),
            Affectations = await _uow.Repository<CategorieSalarie>().ListAsync(a => a.BddSage == sageDb, ct),
            Tolerances = await _uow.Repository<ToleranceRetard>().ListAsync(_ => true, ct),
            Majorations = await _uow.Repository<Majoration>().ListAsync(_ => true, ct),
            Conges = await _leaves.LoadAsync(ct),
            Heures = heures,
            Planning = planning,
            Existantes = existantes,
            Civilites = OvertimeDayRules.ContientHuitMars(from, to)
                ? await ChargerCivilitesAsync(ct)
                : new Dictionary<string, byte?>(StringComparer.Ordinal)
        };
    }

    /// <summary>
    /// Paramétrage du salarié : catégorie (heures hebdo, pause), tolérances, priorité des majorations
    /// et civilité (règle du 8 mars). Les valeurs absentes retombent sur les défauts de la synthèse.
    /// </summary>
    private static OvertimeWeekConfig Configurer(WeeklyContext ctx, CardPaie card)
    {
        var affectation = ctx.Affectations.FirstOrDefault(a => a.MatriculeSage == card.Id);
        var categorie = affectation == null
            ? null
            : ctx.Categories.FirstOrDefault(c => c.IdCategorie == affectation.IdCateg);
        var tolerance = categorie == null
            ? null
            : ctx.Tolerances.FirstOrDefault(t => t.IdCategorie == categorie.IdCategorie && !t.TypesTol);
        ctx.Civilites.TryGetValue(MatriculeIdentity.Cle(card.SageMatricule), out var civilite);

        return new OvertimeWeekConfig(
            MultiPointage: ctx.MultiPointage,
            HeuresSemaine: categorie?.HeureSemaine ?? OvertimeRules.DefaultWeeklyHours,
            PauseCategorie: categorie?.Pause,
            ToleranceEntreeMinutes: tolerance?.Tolerance ?? 0,
            PasSortieMinutes: tolerance?.Sortie,
            CotationNuit: Cotation(ctx.Majorations, "NUIT"),
            CotationDimanche: Cotation(ctx.Majorations, "DIMANCHE"),
            CotationFerie: Cotation(ctx.Majorations, "FERIE"),
            EstFemme: OvertimeDayRules.EstFemme(civilite));
    }

    private static decimal Cotation(IReadOnlyList<Majoration> majorations, string type)
        => majorations
            .Where(m => string.Equals(m.Mojoration?.Trim(), type, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Cotation ?? 0m)
            .DefaultIfEmpty(0m)
            .Max();

    /// <summary>Journées du calcul : paires, planning (reconstruit s'il manque), férié, congé, retard saisi.</summary>
    private static List<OvertimeDayInput> ConstruireJours(WeeklyContext ctx, CardPaie card, DateTime from, DateTime to)
    {
        var cle = MatriculeIdentity.Cle(card.SageMatricule);
        var heures = ctx.Heures.GetValueOrDefault(cle) ?? new List<HeureCorrigee>();
        var planning = ctx.Planning.GetValueOrDefault(card.Id) ?? new List<PlanningSalarie>();
        var conges = ctx.Conges.GetValueOrDefault(cle) ?? new List<LeaveDay>();
        var jours = new List<OvertimeDayInput>();

        // Construction partagée avec la base analytique du moteur prédictif.
        for (var jour = from.Date; jour <= to.Date; jour = jour.AddDays(1))
        {
            jours.Add(OvertimeDayRules.Construire(
                jour,
                heures.Where(h => h.DatePointageIn.HasValue && h.DatePointageIn.Value.Date == jour),
                OvertimeDayRules.PlanDuJour(planning.Where(p => p.DateP.HasValue && p.DateP.Value.Date == jour)),
                ctx.Feries.Any(f => f.Date.HasValue && f.Date.Value.Date == jour),
                LeaveRules.Valeur(conges, jour)));
        }
        return jours;
    }

    /// <summary>La civilité n'est chargée que si la période contient un 8 mars (évite un appel SAGE systématique).</summary>
    private Task<Dictionary<string, byte?>> ChargerCivilitesAsync(CancellationToken ct)
        => SageCivilites.ChargerAsync(_tenant, _externalSource, ct);

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

    /// <summary>
    /// Bornes des lundis (<c>DATE_DEB</c>) des semaines rattachées à une période EXO/IMPO : une semaine
    /// appartient à la période qui contient son dimanche, ce qui l'affecte à une seule période.
    /// </summary>
    private static (DateTime DebutMin, DateTime DebutMax) BornesSemainesDeLaPeriode(DateTime from, DateTime to)
        => (from.Date.AddDays(-6), to.Date.AddDays(-6));

    /// <summary>
    /// Refuse de rouvrir (annuler ou purger) une semaine déjà ventilée dans un calcul EXO/IMPO : ce
    /// calcul a pu être poussé en paie, il faut d'abord purger les heures supplémentaires de la
    /// période qui contient la semaine (même rattachement que <see cref="CalculateExoImpoAsync"/>).
    /// </summary>
    private async Task RefuserSiSemaineCalculeeAsync(
        string sageDb, DateTime lundi, IReadOnlySet<string> cles, CancellationToken ct)
    {
        if (cles.Count == 0)
            return;
        var dimanche = lundi.Date.AddDays(6);
        var calcules = (await _uow.Repository<HsExoImp>().ListAsync(
                h => h.BddSage == sageDb && h.PeriodeDebut <= dimanche && h.PeriodeFin >= dimanche, ct))
            .Where(h => h.Matricule != null && cles.Contains(MatriculeIdentity.Cle(h.Matricule)))
            .OrderBy(h => h.Matricule, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (calcules.Count == 0)
            return;

        var periode = calcules[0];
        var matricules = string.Join(", ", calcules.Select(h => h.Matricule).Distinct().Take(5))
                         + (calcules.Count > 5 ? "…" : string.Empty);
        throw new DomainException(
            $"La semaine du {lundi:dd/MM/yyyy} est incluse dans le calcul EXO/IMPO du "
            + $"{periode.PeriodeDebut:dd/MM/yyyy} au {periode.PeriodeFin:dd/MM/yyyy} ({matricules}) : "
            + "purgez d'abord les heures supplémentaires de cette période.");
    }

    /// <summary>La semaine qui commence ce lundi est-elle rattachée à cette période EXO/IMPO ?</summary>
    private static bool SemaineRattachee(DateTime lundi, DateTime? periodeDebut, DateTime? periodeFin)
    {
        if (!periodeDebut.HasValue || !periodeFin.HasValue)
            return false;
        var dimanche = lundi.Date.AddDays(6);
        return dimanche >= periodeDebut.Value.Date && dimanche <= periodeFin.Value.Date;
    }

    private static void EnsureMondayToSunday(PeriodRequest request)
    {
        if (!OvertimeRules.IsMondayToSundayWeek(request.From, request.To))
            throw new DomainException("La période doit être un lundi–dimanche (7 jours).");
    }

    private static void EnsureMaxOneMonth(PeriodRequest request)
    {
        if (request.To.Date < request.From.Date)
            throw new DomainException("La date de début doit être antérieure ou égale à la date de fin.");
        if (!OvertimeRules.IsMaxOneMonth(request.From, request.To))
            throw new DomainException("La période sélectionnée ne doit pas dépasser un mois.");
    }

    /// <summary>
    /// Cartes du périmètre. Le filtre pointeuse est tolérant (lignes héritées sans base renseignée)
    /// et identique à celui du pré-traitement des pointages.
    /// </summary>
    private async Task<List<CardPaie>> TargetCardsAsync(PeriodRequest request, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        var pointeuseDb = _tenant.PointeuseDb;
        var cards = await _uow.Repository<CardPaie>().ListAsync(c =>
            c.SageBdd == sageDb
            && (string.IsNullOrEmpty(request.Departement)
                || string.Equals(c.Departement, request.Departement, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrEmpty(request.MatriculeFrom) || string.Compare(c.SageMatricule, request.MatriculeFrom) >= 0)
            && (string.IsNullOrEmpty(request.MatriculeTo) || string.Compare(c.SageMatricule, request.MatriculeTo) <= 0), ct);
        return cards
            .Where(c => string.IsNullOrWhiteSpace(c.PointeuseBdd)
                        || string.Equals(c.PointeuseBdd.Trim(), pointeuseDb.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(c => !string.IsNullOrWhiteSpace(c.SageMatricule))
            .GroupBy(c => MatriculeIdentity.Cle(c.SageMatricule))
            .Select(g => g.OrderBy(c => c.Id).First())
            .ToList();
    }
}
