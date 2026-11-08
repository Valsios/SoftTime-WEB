using System.Linq.Expressions;

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

        var list = await _uow.Repository<HsExoImp>().ListReadOnlyAsync(Filtre(request, null), ct);
        if (!string.IsNullOrEmpty(request.Departement))
        {
            list = list.Where(h => h.Matricule != null && targetMatricules.Contains(h.Matricule)).ToList();
        }

        return list
            .OrderBy(h => h.Matricule)
            .Select(ToDto)
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

        List<string>? targetMatricules = null;
        if (!string.IsNullOrEmpty(request.Departement))
        {
            targetMatricules = (await _overtimeWeekContext.TargetCardsAsync(request, ct))
                .Select(c => c.SageMatricule)
                .Where(m => !string.IsNullOrEmpty(m))
                .Select(m => m!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return await PagedLists.From(_uow.Repository<HsExoImp>(), Filtre(request, targetMatricules), q => q.OrderBy(h => h.Matricule).ThenBy(h => h.Oid)).WithSearch(request.Search, new Expression<Func<HsExoImp, string?>>[] { h => h.Matricule }).PageAsync(new PageRequest(page, pageSize), ToDto, ct);
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
        var periode = new ExoImpoPeriod(request.From.Date, request.To.Date, _tenant.SageDb, _tenant.PointeuseDb);

        var cards = (await _overtimeWeekContext.TargetCardsAsync(request, ct))
            .OrderBy(c => c.SageMatricule, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var targetCles = ClesCibles(cards);
        var numSalByMat = await ChargerNumSalAsync(ct);
        var validees = await ChargerSemainesValideesAsync(periode, targetCles, ct);
        var absences = await CalculerAbsencesAsync(request, cards, ct);

        var nouvellesSemaines = new List<HsSemaine>();
        var nouvellesExo = new List<HsExoImp>();
        foreach (var card in cards)
        {
            var cle = MatriculeIdentity.Cle(card.SageMatricule);
            var semaines = validees.GetValueOrDefault(cle) ?? new List<HsValidee>();
            var splits = semaines.Select(w => OvertimeRules.SplitWeekly(w.Hs ?? 0)).ToList();
            var salarie = new ExoImpoEmployee(card.SageMatricule, numSalByMat.TryGetValue(cle, out var n) ? n : null);
            nouvellesSemaines.AddRange(ConstruireSemaines(periode, salarie, splits));

            var absence = absences.GetValueOrDefault(cle);
            if (semaines.Count == 0 && absence == 0m)
                continue;
            var exo = ConstruireExo(periode, salarie, semaines, splits);
            exo.Absence = absence;
            nouvellesExo.Add(exo);
        }

        await RemplacerAsync(periode, targetCles, nouvellesSemaines, nouvellesExo, ct);
        return nouvellesExo.Select(ToDto).ToList();
    }

    /// <summary>Clés matricule non vides des cartes ciblées.</summary>
    private static HashSet<string> ClesCibles(List<CardPaie> cards)
    {
        var targetCles = cards
            .Select(c => MatriculeIdentity.Cle(c.SageMatricule))
            .Where(cle => cle.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        return targetCles;
    }

    private Expression<Func<HsExoImp, bool>> Filtre(PeriodRequest request, List<string>? targetMatricules)
    {
        var sageDb = _tenant.SageDb;
        var from = request.From.Date;
        var to = request.To.Date;
        var matriculeFrom = request.MatriculeFrom;
        var matriculeTo = request.MatriculeTo;
        if (targetMatricules is null)
        {
            return h => h.BddSage == sageDb
                        && h.PeriodeDebut <= to
                        && h.PeriodeFin >= from
                        && (string.IsNullOrEmpty(matriculeFrom) || string.Compare(h.Matricule, matriculeFrom) >= 0)
                        && (string.IsNullOrEmpty(matriculeTo) || string.Compare(h.Matricule, matriculeTo) <= 0);
        }
        return h => h.BddSage == sageDb
                    && h.PeriodeDebut <= to
                    && h.PeriodeFin >= from
                    && (string.IsNullOrEmpty(matriculeFrom) || string.Compare(h.Matricule, matriculeFrom) >= 0)
                    && (string.IsNullOrEmpty(matriculeTo) || string.Compare(h.Matricule, matriculeTo) <= 0)
                    && targetMatricules.Contains(h.Matricule!);
    }

    /// <summary>
    /// Une semaine validée appartient à UNE seule période : celle qui contient son dimanche
    /// (le desktop passait par T_SemainePeriode). Une période démarrant un mercredi inclut donc
    /// sa première semaine, mais la semaine qui déborde sur la période suivante n'est payée
    /// qu'avec celle-ci : sinon elle serait comptée dans deux T_HSExoImp consécutifs.
    /// </summary>
    private async Task<Dictionary<string, List<HsValidee>>> ChargerSemainesValideesAsync(
        ExoImpoPeriod periode, HashSet<string> targetCles, CancellationToken ct)
    {
        var sageDb = periode.SageDb;
        var (debutSemaineMin, debutSemaineMax) = _overtimeWeekContext.BornesSemainesDeLaPeriode(periode.From, periode.To);
        return (await _uow.Repository<HsValidee>().ListAsync(
                h => h.BddSage == sageDb
                     && h.ValidationHs == true
                     && h.DateDeb != null
                     && h.DateDeb >= debutSemaineMin
                     && h.DateDeb <= debutSemaineMax, ct))
            .Where(h => !string.IsNullOrEmpty(h.Matricules) && targetCles.Contains(MatriculeIdentity.Cle(h.Matricules)))
            .GroupBy(h => MatriculeIdentity.Cle(h.Matricules), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.DateDeb).ToList(), StringComparer.Ordinal);
    }

    /// <summary>Absence de la période : calculée sur le planning, indépendamment de la validation.</summary>
    private async Task<Dictionary<string, decimal>> CalculerAbsencesAsync(
        PeriodRequest request, List<CardPaie> cards, CancellationToken ct)
    {
        var from = request.From.Date;
        var to = request.To.Date;
        var ctx = await _overtimeWeekContext.ChargerContexteAsync(request, avecExistantes: false, ct);
        return cards.ToDictionary(
            c => MatriculeIdentity.Cle(c.SageMatricule),
            c => Arrondir(_overtimeWeekContext.ConstruireJours(ctx, c, from, to)
                .Sum(j => OvertimeCalculator.CalculerJour(_overtimeWeekContext.Configurer(ctx, c), j).Absence)),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// PeriodeDebut/PeriodeFin portent la PÉRIODE du calcul (clé de la purge, de la liste et de la
    /// synchro paie, et compatibilité desktop) ; les bornes lundi → dimanche de chaque semaine sont
    /// déduites à la lecture du détail (voir ConstruireSemainesDetail).
    /// </summary>
    private static IEnumerable<HsSemaine> ConstruireSemaines(
        ExoImpoPeriod periode, ExoImpoEmployee salarie, List<HsWeekSplit> splits) =>
        splits.Select((split, index) => new HsSemaine
        {
            Matricule = salarie.Matricule,
            SalNum = salarie.NumSal,
            TotalHeure = split.Hs,
            Hs = split.Hs,
            Hs1 = split.Hs1,
            Hs2 = split.Hs2,
            PeriodeDebut = periode.From,
            PeriodeFin = periode.To,
            Semaine = index + 1,
            BddSage = periode.SageDb,
            BddPointeuse = periode.PointeuseDb
        });

    private static HsExoImp ConstruireExo(
        ExoImpoPeriod periode, ExoImpoEmployee salarie, List<HsValidee> semaines, List<HsWeekSplit> splits)
    {
        var exo = OvertimeRules.AllocateExempt(splits);
        return new HsExoImp
        {
            Matricule = salarie.Matricule,
            NumSal = salarie.NumSal,
            Exo130 = exo.Exo130,
            Exo150 = exo.Exo150,
            I130 = exo.Impo130,
            I150 = exo.Impo150,
            PeriodeDebut = periode.From,
            PeriodeFin = periode.To,
            Nuit = Arrondir(semaines.Sum(x => x.Nuit ?? 0m)),
            Dim = Arrondir(semaines.Sum(x => x.Dimanche ?? 0m)),
            Ferie = Arrondir(semaines.Sum(x => x.Feries ?? 0m)),
            Retard = Arrondir(semaines.Sum(x => x.Retard ?? 0m)),
            BddSage = periode.SageDb,
            BddPointeuse = periode.PointeuseDb
        };
    }

    /// <summary>Remplace les lignes de la période des salariés ciblés par celles recalculées.</summary>
    private async Task RemplacerAsync(
        ExoImpoPeriod periode, HashSet<string> targetCles, List<HsSemaine> semaines, List<HsExoImp> exo, CancellationToken ct)
    {
        var (sageDb, from, to) = (periode.SageDb, periode.From, periode.To);
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

        if (anciennesSemaines.Count > 0)
            semaineRepo.RemoveRange(anciennesSemaines);
        if (anciennesExo.Count > 0)
            exoRepo.RemoveRange(anciennesExo);
        if (semaines.Count > 0)
            await semaineRepo.AddRangeAsync(semaines, ct);
        if (exo.Count > 0)
            await exoRepo.AddRangeAsync(exo, ct);
        await _uow.SaveChangesAsync(ct);
    }

    private static HsExoDto ToDto(HsExoImp h) => new(
        h.Oid, h.Matricule, h.NumSal, h.Exo130, h.Exo150, h.I130, h.I150,
        h.Ferie, h.Nuit, h.Dim, h.Retard, h.Absence);

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
