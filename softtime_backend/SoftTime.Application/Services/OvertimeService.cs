using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.Sage;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

public class OvertimeService
{
    private readonly IUnitOfWork _uow;
    private readonly TenantConnectionService _tenant;
    private readonly IExternalSourceService _externalSource;
    private readonly PayrollWriterResolver _payrollWriters;
    private readonly HistoryArchiveService _history;
    private readonly SageLeaveCalendar _leaves;

    public OvertimeService(IUnitOfWork uow, TenantConnectionService tenant, IExternalSourceService externalSource, PayrollWriterResolver payrollWriters, HistoryArchiveService history, SageLeaveCalendar leaves)
    {
        _uow = uow;
        _tenant = tenant;
        _externalSource = externalSource;
        _payrollWriters = payrollWriters;
        _history = history;
        _leaves = leaves;
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
    /// écrire à chaque changement de page. Les lignes déjà validées sont exclues par le calcul.
    /// </summary>
    public async Task<PagedResult<WeeklyHsDto>> PreviewWeekPagedAsync(
        PeriodRequest request, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        EnsureMondayToSunday(request);
        var rows = await ComputeWeekAsync(request, persistNonValidated: false, ct);
        return PagedLists.Page(rows, page, pageSize);
    }

    public async Task ValidateWeekAsync(WeeklyValidationRequest request, bool validate, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var selected = new HashSet<string>(
            (request.SelectedMatricules ?? Array.Empty<string>()).Where(m => !string.IsNullOrWhiteSpace(m)),
            StringComparer.OrdinalIgnoreCase);
        var rows = await ComputeWeekAsync(request, persistNonValidated: true, ct);
        var v = _uow.Repository<T_HS_VALIDER>();
        var corr = _uow.Repository<T_HEURECORRIGER>();
        foreach (var row in rows)
        {
            var isSelected = row.Matricule != null && selected.Contains(row.Matricule);
            if (!validate && !isSelected)
                continue;
            var flag = validate && isSelected;
            var existing = (await v.ListAsync(x =>
                x.MATRICULES == row.Matricule && x.DATE_DEB == request.From.Date && x.BDD_SAGE == _tenant.SageDb, ct)).FirstOrDefault();
            if (validate && existing?.VALIDATION_HS == true)
                continue;
            if (existing != null)
            {
                existing.VALIDATION_HS = flag;
                existing.DATE_FIN = request.To.Date;
                existing.HT = row.Ht;
                existing.HS = row.Hs;
                existing.NUIT = row.Nuit;
                existing.DIMANCHE = row.Dimanche;
                existing.FERIES = row.Feries;
                existing.RETARD = row.Retard;
                existing.ABSENCE = row.Absence;
                v.Update(existing);
            }
            else
            {
                await v.AddAsync(new T_HS_VALIDER
                {
                    BDD_SAGE = _tenant.SageDb,
                    BDD_POINTAGE = _tenant.PointeuseDb,
                    MATRICULES = row.Matricule,
                    DATE_DEB = request.From.Date,
                    DATE_FIN = request.To.Date,
                    HT = row.Ht,
                    HS = row.Hs,
                    NUIT = row.Nuit,
                    DIMANCHE = row.Dimanche,
                    FERIES = row.Feries,
                    RETARD = row.Retard,
                    ABSENCE = row.Absence,
                    VALIDATION_HS = flag
                }, ct);
            }
            var hours = await corr.ListAsync(h =>
                h.MATRICULE_SAGE == row.Matricule && h.BDD_SAGE == _tenant.SageDb
                && h.DATE_POINTAGE_IN >= request.From && h.DATE_POINTAGE_IN <= request.To, ct);
            foreach (var h in hours)
            {
                h.VALIDER_HS = flag;
                corr.Update(h);
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

        var numSalByMat = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var sageRow = await _tenant.GetSageRowAsync(ct);
            foreach (var emp in await _externalSource.GetEmployeesAsync(sageRow, ct))
            {
                var mat = emp.Matricule?.Trim();
                if (!string.IsNullOrEmpty(mat))
                    numSalByMat[mat] = (int)emp.EmployeeId;
            }
        }
        catch { /* SAGE optional for preview */ }

        // Chargements groupés : une requête par table, pas une par salarié (N+1).
        var weeksByMatricule = (await _uow.Repository<T_HSSemaine>().ListAsync(
                h => h.BDD_SAGE == sageDb && h.PeriodeDebut >= from && h.PeriodeDebut <= to, ct))
            .Where(h => !string.IsNullOrEmpty(h.Matricule))
            .GroupBy(h => h.Matricule!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var validatedByMatricule = (await _uow.Repository<T_HS_VALIDER>().ListAsync(
                h => h.BDD_SAGE == sageDb && h.DATE_DEB >= from && h.DATE_FIN <= to, ct))
            .Where(h => !string.IsNullOrEmpty(h.MATRICULES))
            .GroupBy(h => h.MATRICULES!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.DATE_DEB).ToList(), StringComparer.OrdinalIgnoreCase);

        var exoRepo = _uow.Repository<T_HSExoImp>();
        var existingExoByMatricule = (await exoRepo.ListAsync(
                h => h.BDD_SAGE == sageDb && h.PeriodeDebut == from && h.PeriodeFin == to, ct))
            .Where(h => !string.IsNullOrEmpty(h.Matricule))
            .GroupBy(h => h.Matricule!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var newWeeks = new List<T_HSSemaine>();
        var newExo = new List<T_HSExoImp>();
        var oldExo = new List<T_HSExoImp>();

        foreach (var card in cards)
        {
            var mat = card.SAGE_MATRICULE ?? string.Empty;
            var weeks = weeksByMatricule.GetValueOrDefault(mat) ?? new List<T_HSSemaine>();
            var validated = validatedByMatricule.GetValueOrDefault(mat) ?? new List<T_HS_VALIDER>();

            List<HsWeekSplit> splits;
            if (weeks.Count == 0)
            {
                splits = new List<HsWeekSplit>();
                var i = 1;
                foreach (var w in validated)
                {
                    var split = OvertimeRules.SplitWeekly(w.HS ?? 0);
                    splits.Add(split);
                    newWeeks.Add(new T_HSSemaine
                    {
                        Matricule = card.SAGE_MATRICULE ?? string.Empty,
                        TotalHeure = split.Hs,
                        HS = split.Hs,
                        HS1 = split.Hs1,
                        HS2 = split.Hs2,
                        PeriodeDebut = from,
                        PeriodeFin = to,
                        Semaine = i++,
                        BDD_SAGE = sageDb,
                        BDD_POINTEUSE = _tenant.PointeuseDb
                    });
                }
            }
            else
            {
                splits = weeks.OrderBy(w => w.Semaine)
                    .Select(w => new HsWeekSplit { Hs = w.HS ?? 0, Hs1 = w.HS1 ?? 0, Hs2 = w.HS2 ?? 0 })
                    .ToList();
            }

            var exo = OvertimeRules.AllocateExempt(splits);
            var nuit = validated.Sum(x => x.NUIT ?? 0);
            var dim = validated.Sum(x => x.DIMANCHE ?? 0);
            var ferie = validated.Sum(x => x.FERIES ?? 0);
            var retard = validated.Sum(x => x.RETARD ?? 0);

            if (existingExoByMatricule.TryGetValue(mat, out var olds))
                oldExo.AddRange(olds);

            int? numSal = null;
            var matKey = card.SAGE_MATRICULE?.Trim();
            if (!string.IsNullOrEmpty(matKey) && numSalByMat.TryGetValue(matKey, out var n))
                numSal = n;

            newExo.Add(new T_HSExoImp
            {
                Matricule = card.SAGE_MATRICULE,
                NumSal = numSal,
                EXO130 = exo.Exo130,
                EXO150 = exo.Exo150,
                I130 = exo.Impo130,
                I150 = exo.Impo150,
                PeriodeDebut = from,
                PeriodeFin = to,
                NUIT = nuit,
                DIM = dim,
                FERIE = ferie,
                RETARD = retard,
                BDD_SAGE = sageDb,
                BDD_POINTEUSE = _tenant.PointeuseDb
            });
        }

        if (oldExo.Count > 0)
            exoRepo.RemoveRange(oldExo);
        if (newExo.Count > 0)
            await exoRepo.AddRangeAsync(newExo, ct);
        if (newWeeks.Count > 0)
            await _uow.Repository<T_HSSemaine>().AddRangeAsync(newWeeks, ct);
        await _uow.SaveChangesAsync(ct);

        return newExo
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
        var deverrouillees = 0;
        foreach (var ligne in lignes)
        {
            var heures = await corrRepo.ListAsync(h => h.BDD_SAGE == sageDb
                && h.MATRICULE_SAGE == ligne.MATRICULES
                && h.DATE_POINTAGE_IN >= from && h.DATE_POINTAGE_IN <= to, ct);
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
        return await _payrollWriters.Resolve(sageRow).WriteAsync(sageRow, dtos, ct);
    }

    private async Task<List<WeeklyHsDto>> ComputeWeekAsync(PeriodRequest request, bool persistNonValidated, CancellationToken ct)
    {
        var from = request.From.Date;
        var to = request.To.Date;
        var sageDb = _tenant.SageDb;

        // Chargements groupés : le calcul ne doit pas faire une requête par salarié (N+1).
        var cards = (await TargetCards(request, ct))
            .OrderBy(c => c.SAGE_MATRICULE, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var feries = await _uow.Repository<T_FERIE>().ListAsync(f => f.BDD_SAGE == sageDb, ct);
        var cats = await _uow.Repository<T_CATEGORIE>().ListAsync(c => c.BDD_SAGE == sageDb, ct);
        var affect = await _uow.Repository<T_CAT_SAL>().ListAsync(a => a.BDD_SAGE == sageDb, ct);
        var tols = await _uow.Repository<T_TOLERANCE>().ListAsync(_ => true, ct);
        var leaves = await _leaves.LoadAsync(ct);
        var hsRepo = _uow.Repository<T_HS_VALIDER>();

        var existingByMatricule = (await hsRepo.ListAsync(
                x => x.BDD_SAGE == sageDb && x.DATE_DEB == from, ct))
            .Where(x => !string.IsNullOrEmpty(x.MATRICULES))
            .GroupBy(x => x.MATRICULES!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var hoursByMatricule = (await _uow.Repository<T_HEURECORRIGER>().ListAsync(
                h => h.BDD_SAGE == sageDb
                     && h.MATRICULE_SAGE != null
                     && h.DATE_POINTAGE_IN >= from
                     && h.DATE_POINTAGE_IN <= to, ct))
            .GroupBy(h => h.MATRICULE_SAGE!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var planningByCard = (await _uow.Repository<T_PLANNING_SAL>().ListAsync(
                p => p.BDD_SAGE == sageDb && p.DateP >= from && p.DateP <= to, ct))
            .GroupBy(p => p.IDSAL)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<WeeklyHsDto>();
        foreach (var card in cards)
        {
            existingByMatricule.TryGetValue(card.SAGE_MATRICULE ?? string.Empty, out var existing);
            if (existing?.VALIDATION_HS == true)
                continue;

            var hours = hoursByMatricule.GetValueOrDefault(card.SAGE_MATRICULE ?? string.Empty)
                        ?? new List<T_HEURECORRIGER>();
            var planning = planningByCard.GetValueOrDefault(card.ID)
                           ?? new List<T_PLANNING_SAL>();
            var aff = affect.FirstOrDefault(a => a.MATRICULE_SAGE == card.ID);
            var cat = aff == null ? null : cats.FirstOrDefault(c => c.IDCATEGORIE == aff.ID_CATEG);
            var weeklyNeed = cat?.HEURESEMAINE ?? 40;
            var pause = cat?.PAUSE ?? TimeSpan.Zero;
            var conges = leaves.TryGetValue(MatriculeIdentity.Cle(card.SAGE_MATRICULE), out var joursConge)
                ? joursConge
                : new List<LeaveDay>();
            decimal ht = 0, nuit = 0, dim = 0, ferieH = 0, retard = 0, absence = 0;
            foreach (var day in Enumerable.Range(0, 7).Select(i => request.From.Date.AddDays(i)))
            {
                var plan = planning.FirstOrDefault(p => p.DateP == day);
                var correctionsDuJour = hours
                    .Where(h => h.DATE_POINTAGE_IN.HasValue && h.DATE_POINTAGE_IN.Value.Date == day)
                    .OrderBy(h => h.HENTREE)
                    .ToList();
                var isFerie = feries.Any(f => f.DATE.HasValue && f.DATE.Value.Date == day);
                // Un jour de congé SAGE n'est pas une absence : le desktop le traitait aussi à part
                // (Calcul_HSService : IsConge => la journée est réputée travaillée, jamais absente).
                var estConge = LeaveRules.EstConge(conges, day);

                var paires = correctionsDuJour
                    .Where(h => h.HENTREE.HasValue && h.HSORTIE.HasValue)
                    .Select(h => new PunchPair(
                        h.DATE_POINTAGE_IN!.Value.Date,
                        h.HENTREE!.Value,
                        (h.DATE_POINTAGE_OUT ?? h.DATE_POINTAGE_IN)!.Value.Date,
                        h.HSORTIE!.Value))
                    .ToList();
                if (plan?.HA != null && paires.Count > 0)
                {
                    var heurePlanifiee = plan.HA.Value.TimeOfDay;
                    paires = paires
                        .Select(p => p.EntreeDate.Date == day && p.EntreeHeure < heurePlanifiee
                            ? p with { EntreeHeure = heurePlanifiee }
                            : p)
                        .ToList();
                }

                if (paires.Count > 0)
                {
                    // Plusieurs paires le même jour (pause déjeuner) : on somme leurs durées.
                    var synthese = CorrectionDayRules.Summarize(paires, pause);
                    var heuresJour = synthese.HeuresTravaillees;
                    ht += (decimal)heuresJour.TotalHours;
                    nuit += (decimal)synthese.HeuresNuit.TotalHours;
                    if (day.DayOfWeek == DayOfWeek.Sunday)
                        dim += (decimal)heuresJour.TotalHours;
                    if (isFerie)
                        ferieH += (decimal)heuresJour.TotalHours;

                    var retardExplicite = correctionsDuJour.FirstOrDefault(h => h.RETARD.HasValue)?.RETARD;
                    if (retardExplicite.HasValue)
                    {
                        retard += (decimal)retardExplicite.Value.TotalHours;
                    }
                    else if (plan?.HA != null && synthese.Entree.HasValue)
                    {
                        var tol = tols.FirstOrDefault(t => cat != null && t.IDCATEGORIE == cat.IDCATEGORIE && t.TYPES_TOL == false);
                        var allowed = plan.HA.Value.TimeOfDay.Add(TimeSpan.FromMinutes(tol?.TOLERANCE ?? 0));
                        if (synthese.Entree.Value > allowed)
                            retard += (decimal)(synthese.Entree.Value - allowed).TotalHours;
                    }
                }
                else if (!estConge && plan is { OffP: not true, HA: not null, HD: not null })
                {
                    var abs = plan.HD.Value.TimeOfDay - plan.HA.Value.TimeOfDay - pause;
                    if (abs > TimeSpan.Zero)
                        absence += (decimal)abs.TotalHours;
                }
            }
            var hs = Math.Max(0, ht - weeklyNeed);
            if (persistNonValidated)
            {
                if (existing != null)
                {
                    existing.BDD_POINTAGE = _tenant.PointeuseDb;
                    existing.DATE_FIN = request.To.Date;
                    existing.HT = ht;
                    existing.HS = hs;
                    existing.NUIT = nuit;
                    existing.DIMANCHE = dim;
                    existing.FERIES = ferieH;
                    existing.RETARD = retard;
                    existing.ABSENCE = absence;
                    existing.VALIDATION_HS = false;
                    hsRepo.Update(existing);
                }
                else
                {
                    existing = new T_HS_VALIDER
                    {
                        BDD_SAGE = _tenant.SageDb,
                        BDD_POINTAGE = _tenant.PointeuseDb,
                        MATRICULES = card.SAGE_MATRICULE,
                        DATE_DEB = request.From.Date,
                        DATE_FIN = request.To.Date,
                        HT = ht,
                        HS = hs,
                        NUIT = nuit,
                        DIMANCHE = dim,
                        FERIES = ferieH,
                        RETARD = retard,
                        ABSENCE = absence,
                        VALIDATION_HS = false
                    };
                    await hsRepo.AddAsync(existing, ct);
                }
            }
            result.Add(new WeeklyHsDto(existing?.ID ?? 0, card.SAGE_MATRICULE, request.From.Date, request.To.Date, ht, hs, nuit, dim, ferieH, retard, absence, false));
        }
        await _uow.SaveChangesAsync(ct);
        return result;
    }

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

    private async Task<List<T_CARDPAIE>> TargetCards(PeriodRequest request, CancellationToken ct)
    {
        var cards = await _uow.Repository<T_CARDPAIE>().ListAsync(c =>
            c.SAGE_BDD == _tenant.SageDb
            && (string.IsNullOrEmpty(request.Branche) || c.BRANCHE == request.Branche)
            && (string.IsNullOrEmpty(request.Departement) || c.DEPARTEMENT == request.Departement)
            && (string.IsNullOrEmpty(request.MatriculeFrom) || string.Compare(c.SAGE_MATRICULE, request.MatriculeFrom) >= 0)
            && (string.IsNullOrEmpty(request.MatriculeTo) || string.Compare(c.SAGE_MATRICULE, request.MatriculeTo) <= 0), ct);
        return cards;
    }

    /// <summary>
    /// Détail du calcul des heures supplémentaires d'un salarié : jours issus des heures corrigées,
    /// répartition hebdomadaire EXO/IMPO et allocation enregistrée. Reprend l'intention du
    /// <c>DetailHS</c> du desktop, qui lisait la table brute de la pointeuse et dont le
    /// <c>Include</c> était invalide : la source est ici la chaîne de traitement de l'application.
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
        var cards = await _uow.Repository<T_CARDPAIE>().ListAsync(c => c.SAGE_BDD == _tenant.SageDb, ct);
        var card = cards.FirstOrDefault(c => MatriculeIdentity.Cle(c.SAGE_MATRICULE) == cle);
        var matriculeCarte = card?.SAGE_MATRICULE ?? matricule;

        var corrections = await _uow.Repository<T_HEURECORRIGER>().ListAsync(h =>
            h.BDD_SAGE == _tenant.SageDb
            && h.MATRICULE_SAGE == matriculeCarte
            && h.DATE_POINTAGE_IN != null
            && h.DATE_POINTAGE_IN >= debut && h.DATE_POINTAGE_IN < fin.AddDays(1), ct);
        var feries = await _uow.Repository<T_FERIE>().ListAsync(f => f.BDD_SAGE == _tenant.SageDb, ct);
        var conges = (await _leaves.LoadAsync(ct)).TryGetValue(cle, out var jours) ? jours : new List<LeaveDay>();

        var pause = TimeSpan.Zero;
        if (card is not null)
        {
            var affectations = await _uow.Repository<T_CAT_SAL>().ListAsync(a => a.BDD_SAGE == _tenant.SageDb && a.MATRICULE_SAGE == card.ID, ct);
            var categories = await _uow.Repository<T_CATEGORIE>().ListAsync(c => c.BDD_SAGE == _tenant.SageDb, ct);
            var categorie = categories.FirstOrDefault(c => affectations.Any(a => a.ID_CATEG == c.IDCATEGORIE));
            pause = categorie?.PAUSE ?? TimeSpan.Zero;
        }

        var joursDetail = new List<OvertimeDetailDayDto>();
        for (var day = debut; day <= fin; day = day.AddDays(1))
        {
            var correctionsDuJour = corrections
                .Where(h => h.DATE_POINTAGE_IN.HasValue && h.DATE_POINTAGE_IN.Value.Date == day)
                .ToList();
            var paires = correctionsDuJour
                .Where(h => h.HENTREE.HasValue && h.HSORTIE.HasValue)
                .Select(h => new PunchPair(
                    h.DATE_POINTAGE_IN!.Value.Date,
                    h.HENTREE!.Value,
                    (h.DATE_POINTAGE_OUT ?? h.DATE_POINTAGE_IN)!.Value.Date,
                    h.HSORTIE!.Value))
                .ToList();
            var synthese = CorrectionDayRules.Summarize(paires, pause);
            var entree = synthese.Entree;
            var sortie = synthese.Sortie;
            var jourTravaille = synthese.NombrePaires > 0;
            var heures = synthese.HeuresTravaillees;
            var heuresNuit = synthese.HeuresNuit;
            var retard = correctionsDuJour.FirstOrDefault(h => h.RETARD.HasValue)?.RETARD;
            joursDetail.Add(new OvertimeDetailDayDto(
                day,
                entree,
                sortie,
                Math.Round((decimal)heures.TotalHours, 2),
                Math.Round(OvertimeRules.HeuresSupJournalieres(heures), 2),
                Math.Round((decimal)heuresNuit.TotalHours, 2),
                day.DayOfWeek == DayOfWeek.Sunday,
                feries.Any(f => f.DATE.HasValue && f.DATE.Value.Date == day),
                retard,
                correctionsDuJour.Any(h => h.ABSENCE_AM == true),
                correctionsDuJour.Any(h => h.ABSENCE_PM == true),
                LeaveRules.EstConge(conges, day),
                jourTravaille));
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
}
