using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Closing;

/// <summary>
/// Lectures ensemblistes de la clôture, restreintes aux salariés retenus. Planning, pointages,
/// corrections et semaines validées sont projetés sur les seules colonnes utiles aux règles : ces
/// objets ne sont pas suivis par le contexte (lecture seule, mémoire réduite).
/// </summary>
internal static class ClosingDataQueries
{
    /// <summary>Filtre tenant pointeuse, en tolérant les lignes héritées sans base renseignée.</summary>
    internal static bool CorrespondPointeuse(string? valeur, string pointeuseDb) =>
        string.IsNullOrWhiteSpace(valeur)
        || string.Equals(valeur.Trim(), pointeuseDb.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Cartes de la clôture : base SAGE et pointeuse actives, matricule renseigné, département
    /// demandé.</summary>
    internal static async Task<List<CardPaie>> CartesAsync(
        IUnitOfWork uow, ITenantConnectionService tenant, string? departement, CancellationToken ct)
    {
        var (sageDb, pointeuseDb) = (tenant.SageDb, tenant.PointeuseDb);
        return (await uow.Repository<CardPaie>().ListAsync(c => c.SageBdd == sageDb, ct))
            .Where(c => CorrespondPointeuse(c.PointeuseBdd, pointeuseDb))
            .Where(c => !string.IsNullOrWhiteSpace(c.SageMatricule))
            .Where(c => string.IsNullOrWhiteSpace(departement)
                        || string.Equals(c.Departement?.Trim(), departement.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Marge d'un jour de planning de chaque côté (postes de nuit), comme le pré-traitement.</summary>
    internal static async Task<List<PlanningSalarie>> PlanningsAsync(IUnitOfWork uow, ClosingLoadScope scope, CancellationToken ct)
    {
        var (sageDb, planDebut, planFin) = (scope.SageDb, scope.Debut.AddDays(-1), scope.Fin.AddDays(1));
        var repo = uow.Repository<PlanningSalarie>();
        return (await repo.ToListAsync(repo.Query()
                .Where(p => p.BddSage == sageDb && p.DateP >= planDebut && p.DateP <= planFin)
                .OrderBy(p => p.Id)
                .Select(p => new PlanningSalarie
                {
                    Id = p.Id, IdSal = p.IdSal, DateP = p.DateP, Ha = p.Ha, Hd = p.Hd, Pause = p.Pause, OffP = p.OffP
                }), ct))
            .Where(p => p.DateP.HasValue && scope.IdsCartes.Contains(p.IdSal))
            .ToList();
    }

    /// <summary>Sorties de nuit : les pointages du lendemain de la fin de période sont aussi chargés.</summary>
    internal static async Task<List<Pointage>> PointagesAsync(IUnitOfWork uow, ClosingLoadScope scope, CancellationToken ct)
    {
        var (sageDb, debut, finExclusive) = (scope.SageDb, scope.Debut, scope.Fin.AddDays(2));
        var repo = uow.Repository<Pointage>();
        return (await repo.ToListAsync(repo.Query()
                .Where(p => p.NomBddSage == sageDb && p.DatePointage >= debut && p.DatePointage < finExclusive)
                .OrderBy(p => p.IdPointage)
                .Select(p => new Pointage
                {
                    IdPointage = p.IdPointage, MatriculeSage = p.MatriculeSage, DatePointage = p.DatePointage,
                    HeurePointage = p.HeurePointage, NomBddPointeuse = p.NomBddPointeuse
                }), ct))
            .Where(p => CorrespondPointeuse(p.NomBddPointeuse, scope.PointeuseDb))
            .Where(p => scope.Cles.Contains(MatriculeIdentity.Cle(p.MatriculeSage)))
            .ToList();
    }

    internal static async Task<List<HeureCorrigee>> CorrectionsAsync(IUnitOfWork uow, ClosingLoadScope scope, CancellationToken ct)
    {
        var (sageDb, debut, finExclusive) = (scope.SageDb, scope.Debut, scope.Fin.AddDays(1));
        var repo = uow.Repository<HeureCorrigee>();
        return (await repo.ToListAsync(repo.Query()
                .Where(h => h.BddSage == sageDb && h.DatePointageIn >= debut && h.DatePointageIn < finExclusive)
                .OrderBy(h => h.Id)
                .Select(h => new HeureCorrigee
                {
                    Id = h.Id, MatriculeSage = h.MatriculeSage, DatePointageIn = h.DatePointageIn,
                    HEntree = h.HEntree, HSortie = h.HSortie, Hps = h.Hps, Hpe = h.Hpe,
                    AbsenceAm = h.AbsenceAm, AbsencePm = h.AbsencePm, IntituleAbsence = h.IntituleAbsence,
                    Ferier = h.Ferier, FerierAm = h.FerierAm, FerierPm = h.FerierPm, MFeries = h.MFeries,
                    Origine = h.Origine
                }), ct))
            .Where(h => scope.Cles.Contains(MatriculeIdentity.Cle(h.MatriculeSage)))
            .ToList();
    }

    /// <summary>Semaines validées depuis le lundi de la semaine du début de période.</summary>
    internal static async Task<List<HsValidee>> SemainesValideesAsync(IUnitOfWork uow, ClosingLoadScope scope, CancellationToken ct)
    {
        var (sageDb, fin) = (scope.SageDb, scope.Fin);
        var lundiDebut = scope.Debut.AddDays(-(((int)scope.Debut.DayOfWeek + 6) % 7));
        var repo = uow.Repository<HsValidee>();
        return (await repo.ToListAsync(repo.Query()
                .Where(v => v.BddSage == sageDb && v.ValidationHs == true && v.DateDeb >= lundiDebut && v.DateDeb <= fin)
                .OrderBy(v => v.Id)
                .Select(v => new HsValidee { Id = v.Id, Matricules = v.Matricules, DateDeb = v.DateDeb, ValidationHs = v.ValidationHs, Hs = v.Hs }), ct))
            .Where(v => scope.Cles.Contains(MatriculeIdentity.Cle(v.Matricules)))
            .ToList();
    }
}
