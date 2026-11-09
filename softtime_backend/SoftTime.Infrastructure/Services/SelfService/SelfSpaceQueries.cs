using SoftTime.Application.Abstractions.SelfService;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.SelfService;

/// <summary>
/// Lectures de « Mon espace », toutes filtrées sur le salarié connecté : le filtre est poussé dans la requête (base,
/// cartes ou matricules bruts du salarié) puis recontrôlé par la clé canonique du matricule. Lecture seule.
/// </summary>
internal static class SelfSpaceQueries
{
    /// <summary>Semaines VALIDÉES du salarié dont le lundi tombe dans la période.</summary>
    internal static async Task<List<SelfWeekDto>> SemainesValideesAsync(
        IUnitOfWork uow, SelfContext me, SelfPeriodDto periode, CancellationToken ct)
    {
        var (sageDb, lundiMin, fin) = (me.SageDb, SelfSpacePeriodRules.LundiDe(periode.From), periode.To);
        return (await uow.Repository<HsValidee>().ListReadOnlyAsync(
                h => h.BddSage == sageDb && h.ValidationHs == true && h.DateDeb >= lundiMin && h.DateDeb <= fin, ct))
            .Where(h => h.DateDeb.HasValue && EstMoi(h.Matricules, me))
            .OrderBy(h => h.DateDeb)
            .Select(Semaine)
            .ToList();
    }

    /// <summary>Cumuls EXO / IMPO du salarié sur les périodes de calcul qui recoupent la période.</summary>
    internal static async Task<List<SelfExoImpoDto>> ExoImpoAsync(
        IUnitOfWork uow, SelfContext me, SelfPeriodDto periode, CancellationToken ct)
    {
        var (sageDb, debut, fin) = (me.SageDb, periode.From, periode.To);
        return (await uow.Repository<HsExoImp>().ListReadOnlyAsync(
                h => h.BddSage == sageDb && h.PeriodeDebut <= fin && h.PeriodeFin >= debut, ct))
            .Where(h => h.PeriodeDebut.HasValue && h.PeriodeFin.HasValue && EstMoi(h.Matricule, me))
            .OrderBy(h => h.PeriodeDebut)
            .Select(h => new SelfExoImpoDto(h.PeriodeDebut!.Value.Date, h.PeriodeFin!.Value.Date,
                h.Exo130 ?? 0m, h.Exo150 ?? 0m, h.I130 ?? 0m, h.I150 ?? 0m))
            .ToList();
    }

    internal static async Task<List<SelfAnomalyDto>> AnomaliesAsync(
        IUnitOfWork uow, SelfContext me, SelfPeriodDto periode, CancellationToken ct)
    {
        var (sageDb, debut, finExclusive) = (me.SageDb, periode.From, periode.To.AddDays(1));
        var bruts = MatriculesBruts(me);
        return (await uow.Repository<HeureAnomalie>().ListReadOnlyAsync(
                a => a.BddSage == sageDb && a.DatePointageIn >= debut && a.DatePointageIn < finExclusive
                     && a.MatriculeSage != null && bruts.Contains(a.MatriculeSage), ct))
            .Where(a => a.DatePointageIn.HasValue && EstMoi(a.MatriculeSage, me))
            .OrderBy(a => a.DatePointageIn)
            .Select(a => new SelfAnomalyDto(
                a.DatePointageIn!.Value.Date,
                a.AbsAm == true && a.AbsPm == true ? "Absence sans pointage" : "Pointage à vérifier",
                a.H1, a.H2,
                a.Feries == true))
            .ToList();
    }

    /// <summary>Plannings du salarié (par ses cartes) entre le début et la fin exclusive.</summary>
    internal static async Task<List<PlanningSalarie>> PlanningsAsync(
        IUnitOfWork uow, SelfContext me, (DateTime Debut, DateTime FinExclusive) fenetre, CancellationToken ct)
    {
        var (sageDb, debut, finExclusive) = (me.SageDb, fenetre.Debut, fenetre.FinExclusive);
        var ids = me.Cartes.Select(c => c.Id).ToHashSet();
        return (await uow.Repository<PlanningSalarie>().ListReadOnlyAsync(
                p => p.BddSage == sageDb && p.DateP >= debut && p.DateP < finExclusive && ids.Contains(p.IdSal), ct))
            .Where(p => ids.Contains(p.IdSal))
            .ToList();
    }

    /// <summary>Heures corrigées du salarié (par ses matricules bruts) entre le début et la fin exclusive.</summary>
    internal static async Task<List<HeureCorrigee>> CorrectionsAsync(
        IUnitOfWork uow, SelfContext me, (DateTime Debut, DateTime FinExclusive) fenetre, CancellationToken ct)
    {
        var (sageDb, debut, finExclusive) = (me.SageDb, fenetre.Debut, fenetre.FinExclusive);
        var bruts = MatriculesBruts(me);
        return (await uow.Repository<HeureCorrigee>().ListReadOnlyAsync(
                h => h.BddSage == sageDb && h.DatePointageIn >= debut && h.DatePointageIn < finExclusive
                     && h.MatriculeSage != null && bruts.Contains(h.MatriculeSage), ct))
            .Where(h => EstMoi(h.MatriculeSage, me))
            .ToList();
    }

    internal static async Task<HashSet<DateTime>> FeriesAsync(IUnitOfWork uow, SelfContext me, CancellationToken ct)
    {
        var sageDb = me.SageDb;
        return (await uow.Repository<JourFerie>().ListReadOnlyAsync(f => f.BddSage == sageDb, ct))
            .Where(f => f.Date.HasValue)
            .Select(f => f.Date!.Value.Date)
            .ToHashSet();
    }

    /// <summary>Catégorie du salarié par carte et tolérances de retard : mêmes lectures que l'état des
    /// retards.</summary>
    internal static async Task<RetardTolerances> TolerancesAsync(IUnitOfWork uow, SelfContext me, CancellationToken ct)
    {
        var sageDb = me.SageDb;
        var ids = me.Cartes.Select(c => c.Id).ToHashSet();
        var categorieParCarte = (await uow.Repository<CategorieSalarie>().ListReadOnlyAsync(
                a => a.BddSage == sageDb && ids.Contains(a.MatriculeSage), ct))
            .Where(a => ids.Contains(a.MatriculeSage))
            .GroupBy(a => a.MatriculeSage)
            .ToDictionary(g => g.Key, g => g.First().IdCateg);
        var tolerances = await uow.Repository<ToleranceRetard>().ListReadOnlyAsync(_ => true, ct);
        return new RetardTolerances(categorieParCarte, tolerances);
    }

    internal static bool EstMoi(string? matricule, SelfContext me) => MatriculeIdentity.Cle(matricule) == me.Cle;

    private static HashSet<string> MatriculesBruts(SelfContext me) =>
        me.Cartes.Select(c => c.SageMatricule!.Trim()).Append(me.Matricule).ToHashSet(StringComparer.Ordinal);

    private static SelfWeekDto Semaine(HsValidee h)
    {
        var split = OvertimeRules.SplitWeekly(h.Hs ?? 0m);
        var lundi = h.DateDeb!.Value.Date;
        return new SelfWeekDto(lundi, lundi.AddDays(6), h.Ht ?? 0m, h.Hs ?? 0m, split.Hs1, split.Hs2,
            h.Nuit ?? 0m, h.Dimanche ?? 0m, h.Feries ?? 0m, h.Retard ?? 0m, h.Absence ?? 0m);
    }
}
