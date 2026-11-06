using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public sealed class OvertimeExoImpoService : IOvertimeExoImpoService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IExternalSourceService _externalSource;
    private readonly IOvertimeWeekContext _overtimeWeekContext;

    public OvertimeExoImpoService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IExternalSourceService externalSource,
        IOvertimeWeekContext overtimeWeekContext)
    {
        _uow = uow;
        _tenant = tenant;
        _externalSource = externalSource;
        _overtimeWeekContext = overtimeWeekContext;
    }

    public async Task<IReadOnlyList<HsExoDto>> ListExoAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        _overtimeWeekContext.EnsureMaxOneMonth(request);
        var cards = await _overtimeWeekContext.TargetCardsAsync(request, ct);
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
        _overtimeWeekContext.EnsureMaxOneMonth(request);
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

        var cards = await _overtimeWeekContext.TargetCardsAsync(request, ct);
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
        _overtimeWeekContext.EnsureMaxOneMonth(request);
        var sageDb = _tenant.SageDb;
        var from = request.From.Date;
        var to = request.To.Date;

        var cards = (await _overtimeWeekContext.TargetCardsAsync(request, ct))
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
        var (debutSemaineMin, debutSemaineMax) = _overtimeWeekContext.BornesSemainesDeLaPeriode(from, to);
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
        var ctx = await _overtimeWeekContext.ChargerContexteAsync(request, avecExistantes: false, ct);
        var absences = cards.ToDictionary(
            c => MatriculeIdentity.Cle(c.SageMatricule),
            c => Arrondir(_overtimeWeekContext.ConstruireJours(ctx, c, from, to).Sum(j => OvertimeCalculator.CalculerJour(_overtimeWeekContext.Configurer(ctx, c), j).Absence)),
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
}
