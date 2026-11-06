using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Application.SelfService;

/// <summary>
/// Lecture seule des données du salarié connecté. Le matricule vient du <see cref="SelfServiceGuard"/> (donc du jeton) et
/// toute lecture est filtrée sur lui ; aucune écriture, aucun service de traitement, ni <c>ReportService</c> ni le contrôle
/// avant paie. Sources : T_HS_VALIDER (semaines VALIDÉES), T_HSExoImp, T_HEUREANOMALIE par lecture directe ; absences et retards
/// par les règles pures du Domain (AbsenceReportRules, RetardReportRules) ; répartition 130 % / 150 % par OvertimeRules.
/// </summary>
public class SelfSpaceService
{
    /// <summary>Fenêtre consultable, en mois (mois en cours compris) et durée maximale d'une période demandée.</summary>
    public const int FenetreMois = 4;

    private readonly IUnitOfWork _uow;
    private readonly SelfServiceGuard _guard;
    private readonly SageLeaveCalendar _leaves;
    private readonly SelfLeaveCache _cacheConges;
    private readonly TimeProvider _horloge;

    public SelfSpaceService(IUnitOfWork uow, SelfServiceGuard guard, SageLeaveCalendar leaves, SelfLeaveCache cacheConges, TimeProvider? horloge = null)
    {
        _uow = uow;
        _guard = guard;
        _leaves = leaves;
        _cacheConges = cacheConges;
        _horloge = horloge ?? TimeProvider.System;
    }

    private DateTime Aujourdhui() => _horloge.GetLocalNow().Date;

    /// <summary>Période par défaut : le mois en cours jusqu'à aujourd'hui.</summary>
    internal static (DateTime From, DateTime To) PeriodeParDefaut(DateTime aujourdhui)
        => (new DateTime(aujourdhui.Year, aujourdhui.Month, 1), aujourdhui);

    /// <summary>
    /// Borne la période demandée : dates obligatoires et ordonnées, au plus 4 mois, dans la fenêtre des 4 derniers mois
    /// (mois en cours compris), sans jour futur. Une erreur de période est une InvalidOperationException (400).
    /// </summary>
    internal static (DateTime From, DateTime To) Borner(DateTime? from, DateTime? to, DateTime aujourdhui)
    {
        var (f0, t0) = PeriodeParDefaut(aujourdhui);
        var debut = (from ?? f0).Date;
        var fin = (to ?? t0).Date;
        if (fin < debut)
            throw new InvalidOperationException("La date de fin doit être postérieure ou égale à la date de début.");
        var fenetre = new DateTime(aujourdhui.Year, aujourdhui.Month, 1).AddMonths(-(FenetreMois - 1));
        if (debut < fenetre)
            throw new InvalidOperationException($"Seuls les {FenetreMois} derniers mois sont consultables.");
        if (fin > aujourdhui)
            fin = aujourdhui;
        if (debut > fin)
            throw new InvalidOperationException("La période demandée est dans le futur.");
        if (fin > debut.AddMonths(FenetreMois).AddDays(-1))
            throw new InvalidOperationException($"La période ne peut pas dépasser {FenetreMois} mois.");
        return (debut, fin);
    }

    private static DateTime LundiDe(DateTime jour) => jour.Date.AddDays(-(((int)jour.DayOfWeek + 6) % 7));

    private static bool EstMoi(string? matricule, SelfContext me) => MatriculeIdentity.Cle(matricule) == me.Cle;

    // ----------------------------------------------------------------------------- heures par semaine et EXO / IMPO

    public async Task<SelfWeeksDto> WeeksAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var me = await _guard.ResolveAsync(ct);
        var (debut, fin) = Borner(from, to, Aujourdhui());
        return await SemainesAsync(me, debut, fin, ct);
    }

    private async Task<SelfWeeksDto> SemainesAsync(SelfContext me, DateTime debut, DateTime fin, CancellationToken ct)
    {
        var lundiMin = LundiDe(debut);
        // Semaines VALIDÉES seulement, du salarié connecté, dont le lundi tombe dans la période.
        var validees = (await _uow.Repository<T_HS_VALIDER>().ListAsync(
                h => h.BDD_SAGE == me.SageDb && h.VALIDATION_HS == true && h.DATE_DEB >= lundiMin && h.DATE_DEB <= fin, ct))
            .Where(h => h.DATE_DEB.HasValue && EstMoi(h.MATRICULES, me))
            .OrderBy(h => h.DATE_DEB)
            .ToList();
        var semaines = validees.Select(h =>
        {
            var split = OvertimeRules.SplitWeekly(h.HS ?? 0m);
            var lundi = h.DATE_DEB!.Value.Date;
            return new SelfWeekDto(lundi, lundi.AddDays(6), h.HT ?? 0m, h.HS ?? 0m, split.Hs1, split.Hs2,
                h.NUIT ?? 0m, h.DIMANCHE ?? 0m, h.FERIES ?? 0m, h.RETARD ?? 0m, h.ABSENCE ?? 0m);
        }).ToList();

        var exo = (await _uow.Repository<T_HSExoImp>().ListAsync(
                h => h.BDD_SAGE == me.SageDb && h.PeriodeDebut <= fin && h.PeriodeFin >= debut, ct))
            .Where(h => h.PeriodeDebut.HasValue && h.PeriodeFin.HasValue && EstMoi(h.Matricule, me))
            .OrderBy(h => h.PeriodeDebut)
            .Select(h => new SelfExoImpoDto(h.PeriodeDebut!.Value.Date, h.PeriodeFin!.Value.Date,
                h.EXO130 ?? 0m, h.EXO150 ?? 0m, h.I130 ?? 0m, h.I150 ?? 0m))
            .ToList();
        return new SelfWeeksDto(new SelfPeriodDto(debut, fin), semaines, exo);
    }

    // ----------------------------------------------------------------------------- anomalies

    public async Task<SelfAnomaliesDto> AnomaliesAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var me = await _guard.ResolveAsync(ct);
        var (debut, fin) = Borner(from, to, Aujourdhui());
        return new SelfAnomaliesDto(new SelfPeriodDto(debut, fin), await AnomaliesListAsync(me, debut, fin, ct));
    }

    private async Task<IReadOnlyList<SelfAnomalyDto>> AnomaliesListAsync(SelfContext me, DateTime debut, DateTime fin, CancellationToken ct)
    {
        var finExclusive = fin.AddDays(1);
        return (await _uow.Repository<T_HEUREANOMALIE>().ListAsync(
                a => a.BDD_SAGE == me.SageDb && a.DATE_POINTAGE_IN >= debut && a.DATE_POINTAGE_IN < finExclusive, ct))
            .Where(a => a.DATE_POINTAGE_IN.HasValue && EstMoi(a.MATRICULE_SAGE, me))
            .OrderBy(a => a.DATE_POINTAGE_IN)
            .Select(a => new SelfAnomalyDto(
                a.DATE_POINTAGE_IN!.Value.Date,
                a.ABS_AM == true && a.ABS_PM == true ? "Absence sans pointage" : "Pointage à vérifier",
                a.H1, a.H2,
                a.FERIES == true))
            .ToList();
    }

    // ----------------------------------------------------------------------------- absences et congés

    public async Task<SelfAbsencesDto> AbsencesAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var me = await _guard.ResolveAsync(ct);
        var (debut, fin) = Borner(from, to, Aujourdhui());
        return new SelfAbsencesDto(new SelfPeriodDto(debut, fin), await AbsencesListAsync(me, debut, fin, ct));
    }

    private async Task<IReadOnlyList<SelfAbsenceDto>> AbsencesListAsync(SelfContext me, DateTime debut, DateTime fin, CancellationToken ct)
    {
        var contexte = await ChargerAsync(me, debut, fin, avecConges: true, ct);
        var lignes = AbsenceReportRules.Calculer(
            contexte.Plannings, me.Cartes, contexte.Corrections, contexte.Conges, contexte.Feries, debut, fin, Aujourdhui());
        // Les règles reçoivent uniquement les cartes du salarié : ses lignes sont les seules possibles ; le filtre reste explicite.
        return lignes
            .Where(l => EstMoi(l.Matricule, me))
            .OrderBy(l => l.Date)
            .Select(l => new SelfAbsenceDto(l.Date.Date, l.Periode, l.Type, l.DureeHeures, l.Motif))
            .ToList();
    }

    // ----------------------------------------------------------------------------- retards

    public async Task<SelfDelaysDto> DelaysAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var me = await _guard.ResolveAsync(ct);
        var (debut, fin) = Borner(from, to, Aujourdhui());
        return new SelfDelaysDto(new SelfPeriodDto(debut, fin), await DelaysListAsync(me, debut, fin, ct));
    }

    private async Task<IReadOnlyList<SelfDelayDto>> DelaysListAsync(SelfContext me, DateTime debut, DateTime fin, CancellationToken ct)
    {
        var contexte = await ChargerAsync(me, debut, fin, avecConges: false, ct);
        var ids = me.Cartes.Select(c => c.ID).ToHashSet();
        // Catégorie du salarié (T_CAT_SAL référence T_CARDPAIE.ID) et tolérances : mêmes lectures que l'état des retards.
        var categorieParCarte = (await _uow.Repository<T_CAT_SAL>().ListAsync(a => a.BDD_SAGE == me.SageDb, ct))
            .Where(a => ids.Contains(a.MATRICULE_SAGE))
            .GroupBy(a => a.MATRICULE_SAGE)
            .ToDictionary(g => g.Key, g => g.First().ID_CATEG);
        var tolerances = await _uow.Repository<T_TOLERANCE>().ListAsync(_ => true, ct);
        return RetardReportRules.Calculer(me.Cartes, contexte.Corrections, contexte.Plannings, categorieParCarte, tolerances,
                contexte.Feries, debut, fin, Aujourdhui())
            .Where(r => EstMoi(r.Matricule, me) && r.MinutesRetard > 0)
            .OrderBy(r => r.Date)
            .Select(r => new SelfDelayDto(r.Date.Date, r.HeureEntree, r.HeurePrevue, r.ToleranceMinutes, r.MinutesRetard, r.RetardSaisi))
            .ToList();
    }

    // ----------------------------------------------------------------------------- synthèse

    public async Task<SelfSummaryDto> SummaryAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var me = await _guard.ResolveAsync(ct);
        var (debut, fin) = Borner(from, to, Aujourdhui());
        var semaines = await SemainesAsync(me, debut, fin, ct);
        var anomalies = await AnomaliesListAsync(me, debut, fin, ct);
        var absences = await AbsencesListAsync(me, debut, fin, ct);
        var retards = await DelaysListAsync(me, debut, fin, ct);
        return new SelfSummaryDto(
            new SelfPeriodDto(debut, fin),
            semaines.Semaines.Count,
            semaines.Semaines.Sum(s => s.HeuresSupplementaires),
            semaines.Semaines.Sum(s => s.Hs130),
            semaines.Semaines.Sum(s => s.Hs150),
            anomalies.Count,
            absences.Count(a => a.Type == AbsenceReportRules.TypeNonJustifiee),
            absences.Count(a => a.Type == AbsenceReportRules.TypeCongeSage),
            retards.Count,
            retards.Sum(r => r.MinutesRetard));
    }

    // ----------------------------------------------------------------------------- chargement du salarié

    private sealed record Donnees(
        IReadOnlyList<T_PLANNING_SAL> Plannings,
        IReadOnlyList<T_HEURECORRIGER> Corrections,
        IReadOnlySet<DateTime> Feries,
        IReadOnlyDictionary<string, List<LeaveDay>> Conges);

    /// <summary>
    /// Plannings, corrections, fériés et (si demandé) congés SAGE de CE salarié sur la période, plafonnée à la veille comme les états.
    /// Les lectures sont filtrées sur ses cartes ou sur ses matricules bruts, puis recontrôlées par la clé canonique.
    /// </summary>
    private async Task<Donnees> ChargerAsync(SelfContext me, DateTime debut, DateTime fin, bool avecConges, CancellationToken ct)
    {
        var hier = Aujourdhui().AddDays(-1);
        var finLue = fin < hier ? fin : hier;
        var finExclusive = finLue.AddDays(1);
        var ids = me.Cartes.Select(c => c.ID).ToHashSet();
        var bruts = me.Cartes.Select(c => c.SAGE_MATRICULE!.Trim()).Append(me.Matricule).ToHashSet(StringComparer.Ordinal);

        var plannings = (await _uow.Repository<T_PLANNING_SAL>().ListAsync(
                p => p.BDD_SAGE == me.SageDb && p.DateP >= debut && p.DateP < finExclusive, ct))
            .Where(p => ids.Contains(p.IDSAL))
            .ToList();
        var corrections = (await _uow.Repository<T_HEURECORRIGER>().ListAsync(
                h => h.BDD_SAGE == me.SageDb && h.DATE_POINTAGE_IN >= debut && h.DATE_POINTAGE_IN < finExclusive
                     && h.MATRICULE_SAGE != null && bruts.Contains(h.MATRICULE_SAGE), ct))
            .Where(h => EstMoi(h.MATRICULE_SAGE, me))
            .ToList();
        var feries = (await _uow.Repository<T_FERIE>().ListAsync(f => f.BDD_SAGE == me.SageDb, ct))
            .Where(f => f.DATE.HasValue)
            .Select(f => f.DATE!.Value.Date)
            .ToHashSet();

        IReadOnlyDictionary<string, List<LeaveDay>> conges = new Dictionary<string, List<LeaveDay>>(StringComparer.Ordinal);
        if (avecConges)
        {
            try
            {
                // Le calendrier SAGE lit les congés de tout le tenant (aucune requête filtrée n'existe dans la source) :
                // seule la clé du salarié connecté est conservée, le reste n'est jamais exposé.
                // Mesuré sur la démo : environ 5 s pour tout le tenant. On ne conserve donc que la part du salarié, en cache à son nom.
                var siens = await _cacheConges.ObtenirAsync(SelfLeaveCache.Cle(me.UserId, me.SageDb, me.Cle), _horloge, async () =>
                {
                    var tous = await _leaves.LoadAsync(ct);
                    return tous.TryGetValue(me.Cle, out var s) ? s : new List<LeaveDay>();
                });
                conges = new Dictionary<string, List<LeaveDay>>(StringComparer.Ordinal) { [me.Cle] = siens };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Source SAGE indisponible : les absences sont calculées sans les congés SAGE, comme dans les états.
            }
        }
        return new Donnees(plannings, corrections, feries, conges);
    }
}
