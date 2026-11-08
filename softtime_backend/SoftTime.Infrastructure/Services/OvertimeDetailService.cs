using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public sealed class OvertimeDetailService : IOvertimeDetailService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IHistoryArchiveService _history;
    private readonly IOvertimeWeekContext _overtimeWeekContext;

    public OvertimeDetailService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IHistoryArchiveService history,
        IOvertimeWeekContext overtimeWeekContext)
    {
        _uow = uow;
        _tenant = tenant;
        _history = history;
        _overtimeWeekContext = overtimeWeekContext;
    }

    public async Task PurgeHsAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        _overtimeWeekContext.EnsureMaxOneMonth(request);
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
            var cards = await _overtimeWeekContext.TargetCardsAsync(request, ct);
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
        var ctx = await _overtimeWeekContext.ChargerContexteAsync(new PeriodRequest(null, null, debut, fin), avecExistantes: false, ct);
        var card = ctx.Cards.FirstOrDefault(c => MatriculeIdentity.Cle(c.SageMatricule) == cle);
        var matriculeCarte = card?.SageMatricule ?? matricule;
        var joursDetail = card == null
            ? new List<OvertimeDetailDayDto>()
            : JoursDetail(ctx, card, cle, _overtimeWeekContext.ConstruireJours(ctx, card, debut, fin));

        var lignesSemaine = await ChargerLignesSemaineAsync(matriculeCarte, debut, fin, ct);
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

    /// <summary>Jours de la période passés par le même calculateur que la validation semaine.</summary>
    private List<OvertimeDetailDayDto> JoursDetail(WeeklyContext ctx, CardPaie card, string cle, List<OvertimeDayInput> jours)
    {
        var config = _overtimeWeekContext.Configurer(ctx, card);
        var lignesParJour = (ctx.Heures.GetValueOrDefault(cle) ?? new List<HeureCorrigee>())
            .Where(h => h.DatePointageIn.HasValue)
            .GroupBy(h => h.DatePointageIn!.Value.Date)
            .ToDictionary(g => g.Key, g => g.ToList());
        return jours
            .Select(jour => JourDetail(config, jour, lignesParJour.GetValueOrDefault(jour.Jour) ?? new List<HeureCorrigee>()))
            .ToList();
    }

    private static OvertimeDetailDayDto JourDetail(OvertimeWeekConfig config, OvertimeDayInput jour, List<HeureCorrigee> lignes)
    {
        var resultat = OvertimeCalculator.CalculerJour(config, jour);
        var heuresSupJour = Math.Round(
            OvertimeRules.HeuresSupJournalieres(TimeSpan.FromHours((double)resultat.HeuresRetenues), (double)config.Journee),
            2);
        return new OvertimeDetailDayDto(
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
            resultat.HeuresReelles);
    }

    /// <summary>
    /// T_HSSemaine.PeriodeDebut/PeriodeFin portent la PÉRIODE du calcul (clé de la purge, de la
    /// liste et de la synchro paie, et sémantique du desktop) : les bornes de chaque semaine sont
    /// déduites des semaines validées, sinon les colonnes Début/Fin afficheraient la période.
    /// </summary>
    private async Task<List<HsSemaine>> ChargerLignesSemaineAsync(
        string matriculeCarte, DateTime debut, DateTime fin, CancellationToken ct) =>
        (await _uow.Repository<HsSemaine>().ListAsync(h =>
                h.BddSage == _tenant.SageDb && h.Matricule == matriculeCarte, ct))
            .Where(h => h.PeriodeDebut.HasValue && h.PeriodeFin.HasValue
                        && h.PeriodeDebut!.Value.Date <= fin && h.PeriodeFin!.Value.Date >= debut)
            .OrderBy(h => h.PeriodeDebut)
            .ThenBy(h => h.Semaine)
            .ThenBy(h => h.Oid)
            .ToList();

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

        var (borneBasse, borneHaute) = _overtimeWeekContext.BornesSemainesDeLaPeriode(debuts.Min(), fins.Max());
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
            var (candidates, coherent) = SemainesCandidates(semainesValidees, groupe, lignesGroupe);

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

    /// <summary>Semaines validées rattachées à la période du groupe, et cohérence de leur nombre avec les rangs des lignes.</summary>
    private static (List<HsValidee> Candidates, bool Coherent) SemainesCandidates(
        IReadOnlyList<HsValidee> semainesValidees, IGrouping<(DateTime? Debut, DateTime? Fin), HsSemaine> groupe, List<HsSemaine> lignesGroupe)
    {
        var candidates = groupe.Key.Debut.HasValue && groupe.Key.Fin.HasValue
            ? semainesValidees
                .Where(v => v.DateDeb.HasValue
                            && SemaineRattachee(v.DateDeb.Value, groupe.Key.Debut, groupe.Key.Fin))
                .ToList()
            : new List<HsValidee>();

        var coherent = candidates.Count == lignesGroupe.Count
                       && lignesGroupe.All(h => h.Semaine is int rang && rang >= 1 && rang <= candidates.Count);
        return (candidates, coherent);
    }

    /// <summary>La semaine qui commence ce lundi est-elle rattachée à cette période EXO/IMPO ?</summary>
    private static bool SemaineRattachee(DateTime lundi, DateTime? periodeDebut, DateTime? periodeFin)
    {
        if (!periodeDebut.HasValue || !periodeFin.HasValue)
            return false;
        var dimanche = lundi.Date.AddDays(6);
        return dimanche >= periodeDebut.Value.Date && dimanche <= periodeFin.Value.Date;
    }
}
