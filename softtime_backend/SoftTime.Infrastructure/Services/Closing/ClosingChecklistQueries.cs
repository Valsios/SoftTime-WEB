using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Closing;

/// <summary>
/// Lectures de la checklist, restreintes aux salariés de la clôture (mêmes cartes que l'analyse). Les lignes sont
/// projetées sur les seules colonnes utiles ou comptées : aucune n'est suivie par le contexte.
/// </summary>
internal static class ClosingChecklistQueries
{
    /// <summary>Pointages importés de la période : nombre de pointages et de salariés pointés.</summary>
    internal static async Task<(int Pointages, int Salaries)> PointagesAsync(
        IUnitOfWork uow, ClosingLoadScope scope, CancellationToken ct)
    {
        var (sageDb, debut, finExclusive) = (scope.SageDb, scope.Debut, scope.Fin.AddDays(1));
        var repo = uow.Repository<Pointage>();
        var cles = (await repo.ToListAsync(repo.Query()
                .Where(p => p.NomBddSage == sageDb && p.DatePointage >= debut && p.DatePointage < finExclusive)
                .Select(p => new Pointage
                {
                    MatriculeSage = p.MatriculeSage, NomBddPointeuse = p.NomBddPointeuse
                }), ct))
            .Where(p => ClosingDataQueries.CorrespondPointeuse(p.NomBddPointeuse, scope.PointeuseDb))
            .Select(p => MatriculeIdentity.Cle(p.MatriculeSage))
            .Where(scope.Cles.Contains)
            .ToList();
        return (cles.Count, cles.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>Anomalies restant à arbitrer sur la période.</summary>
    internal static async Task<int> AnomaliesAsync(IUnitOfWork uow, ClosingLoadScope scope, CancellationToken ct)
    {
        var (sageDb, debut, finExclusive) = (scope.SageDb, scope.Debut, scope.Fin.AddDays(1));
        var repo = uow.Repository<HeureAnomalie>();
        return (await repo.ToListAsync(repo.Query()
                .Where(a => a.BddSage == sageDb && a.DatePointageIn >= debut && a.DatePointageIn < finExclusive)
                .Select(a => a.MatriculeSage), ct))
            .Count(m => scope.Cles.Contains(MatriculeIdentity.Cle(m)));
    }

    /// <summary>Heures corrigées de la période : preuve que le traitement des pointages a été lancé.</summary>
    internal static async Task<int> CorrectionsAsync(IUnitOfWork uow, ClosingLoadScope scope, CancellationToken ct)
    {
        var (sageDb, debut, finExclusive) = (scope.SageDb, scope.Debut, scope.Fin.AddDays(1));
        var repo = uow.Repository<HeureCorrigee>();
        return (await repo.ToListAsync(repo.Query()
                .Where(h => h.BddSage == sageDb && h.DatePointageIn >= debut && h.DatePointageIn < finExclusive)
                .Select(h => h.MatriculeSage), ct))
            .Count(m => scope.Cles.Contains(MatriculeIdentity.Cle(m)));
    }

    /// <summary>
    /// Semaines-salarié attendues et validées. Une semaine appartient à la période qui contient son dimanche (comme le
    /// calcul EXO/IMPO) ; elle est attendue si au moins un jour planifié travaillé y tombe.
    /// </summary>
    internal static async Task<(int Attendues, int Validees)> SemainesAsync(
        IUnitOfWork uow, ClosingLoadScope scope, IReadOnlyDictionary<int, string> cleParIdSal, CancellationToken ct)
    {
        var (lundiMin, lundiMax) = (scope.Debut.AddDays(-6), scope.Fin.AddDays(-6));
        var attendues = (await PlanningsAsync(uow, scope, cleParIdSal, ct))
            .GroupBy(p => (Cle: cleParIdSal[p.IdSal], Jour: p.DateP!.Value.Date))
            .Select(g => (g.Key, Retenu: PlanningRetenu(g)))
            .Where(x => !AbsenceReportRules.EstJourOff(x.Retenu)
                        && AbsenceReportRules.CalculerDureeHeures(x.Retenu) is not null)
            .Select(x => (x.Key.Cle, Lundi: LundiDe(x.Key.Jour)))
            .Where(x => x.Lundi >= lundiMin && x.Lundi <= lundiMax)
            .ToHashSet();
        var validees = await SemainesValideesAsync(uow, scope, ct);
        return (attendues.Count, attendues.Count(validees.Contains));
    }

    /// <summary>Lignes EXO/IMPO des salariés retenus sur une période qui recoupe celle de la checklist.</summary>
    internal static async Task<List<HsExoImp>> ExoImpoRecoupantesAsync(
        IUnitOfWork uow, ClosingLoadScope scope, CancellationToken ct)
    {
        var (sageDb, debut, fin) = (scope.SageDb, scope.Debut, scope.Fin);
        return (await uow.Repository<HsExoImp>().ListReadOnlyAsync(
                h => h.BddSage == sageDb && h.PeriodeDebut <= fin && h.PeriodeFin >= debut, ct))
            .Where(h => scope.Cles.Contains(MatriculeIdentity.Cle(h.Matricule)))
            .ToList();
    }

    /// <summary>
    /// Résumé de la dernière synchronisation paie de la période : seule trace stockée, la ligne « SYNC_PAIE » du
    /// journal d'audit pour la même base et la même période.
    /// </summary>
    internal static async Task<string?> DernierResumeSynchroAsync(
        IUnitOfWork uow, ClosingLoadScope scope, CancellationToken ct)
    {
        var sageDb = scope.SageDb;
        var libellePeriode = $"Période={scope.Debut:yyyy-MM-dd}..{scope.Fin:yyyy-MM-dd}";
        return (await uow.Repository<JournalAudit>().ListReadOnlyAsync(
                j => j.Actions == "SYNC_PAIE" && j.BaseSage == sageDb
                     && j.Nouveaux != null && j.Nouveaux.Contains(libellePeriode), ct))
            .OrderByDescending(j => j.DateUtc)
            .Select(j => j.Nouveaux ?? string.Empty)
            .FirstOrDefault();
    }

    private static async Task<List<PlanningSalarie>> PlanningsAsync(
        IUnitOfWork uow, ClosingLoadScope scope, IReadOnlyDictionary<int, string> cleParIdSal, CancellationToken ct)
    {
        var (sageDb, lundiMin, fin) = (scope.SageDb, scope.Debut.AddDays(-6), scope.Fin);
        var repo = uow.Repository<PlanningSalarie>();
        return (await repo.ToListAsync(repo.Query()
                .Where(p => p.BddSage == sageDb && p.DateP >= lundiMin && p.DateP <= fin)
                .Select(p => new PlanningSalarie
                {
                    Id = p.Id, IdSal = p.IdSal, DateP = p.DateP, Ha = p.Ha, Hd = p.Hd, Pause = p.Pause, OffP = p.OffP
                }), ct))
            .Where(p => p.DateP.HasValue && cleParIdSal.ContainsKey(p.IdSal))
            .ToList();
    }

    private static async Task<HashSet<(string Cle, DateTime Lundi)>> SemainesValideesAsync(
        IUnitOfWork uow, ClosingLoadScope scope, CancellationToken ct)
    {
        var (sageDb, lundiMin, lundiMax) = (scope.SageDb, scope.Debut.AddDays(-6), scope.Fin.AddDays(-6));
        return (await uow.Repository<HsValidee>().ListReadOnlyAsync(
                v => v.BddSage == sageDb && v.ValidationHs == true
                     && v.DateDeb >= lundiMin && v.DateDeb <= lundiMax, ct))
            .Where(v => v.DateDeb.HasValue && !string.IsNullOrWhiteSpace(v.Matricules))
            .Select(v => (Cle: MatriculeIdentity.Cle(v.Matricules), Lundi: v.DateDeb!.Value.Date))
            .ToHashSet();
    }

    /// <summary>Ligne de planning retenue pour un jour : une ligne travaillée l'emporte sur un repos (OFF).</summary>
    private static PlanningSalarie PlanningRetenu(IEnumerable<PlanningSalarie> lignes) =>
        lignes.OrderBy(p => AbsenceReportRules.EstJourOff(p) ? 1 : 0).ThenBy(p => p.Id).First();

    private static DateTime LundiDe(DateTime jour) => jour.Date.AddDays(-(((int)jour.DayOfWeek + 6) % 7));
}
