using Microsoft.Extensions.Logging;

using SoftTime.Application.Abstractions.Employees;
using SoftTime.Application.Abstractions.SelfService;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.SelfService;

/// <summary>
/// Lecture seule des données du salarié connecté. Le matricule vient de <see cref="ISelfServiceGuard"/> (donc du jeton)
/// et toute lecture est filtrée sur lui ; aucune écriture, aucun service de traitement, ni l'état des absences ni le
/// contrôle avant paie. Sources : semaines VALIDÉES, cumuls EXO/IMPO et anomalies par lecture directe ; absences et
/// retards par les règles pures du Domain (<see cref="AbsenceReportRules"/>, <see cref="RetardReportRules"/>) ;
/// répartition 130 % / 150 % par <see cref="OvertimeRules"/>.
/// </summary>
public sealed class SelfSpaceService : ISelfSpaceService
{
    private readonly IUnitOfWork _uow;
    private readonly ISelfServiceGuard _guard;
    private readonly ISageLeaveCalendar _leaves;
    private readonly ISelfLeaveCache _cacheConges;
    private readonly TimeProvider _horloge;
    private readonly ILogger<SelfSpaceService> _logger;

    public SelfSpaceService(
        IUnitOfWork uow,
        ISelfServiceGuard guard,
        ISageLeaveCalendar leaves,
        ISelfLeaveCache cacheConges,
        TimeProvider horloge,
        ILogger<SelfSpaceService> logger)
    {
        _uow = uow;
        _guard = guard;
        _leaves = leaves;
        _cacheConges = cacheConges;
        _horloge = horloge;
        _logger = logger;
    }

    /// <summary>Synthèse de la période (mois en cours par défaut) : comptages seulement.</summary>
    public async Task<SelfSummaryDto> SummaryAsync(SelfPeriodQuery query, CancellationToken ct = default)
    {
        var (me, periode) = await ResoudreAsync(query, ct);
        var semaines = await SelfSpaceQueries.SemainesValideesAsync(_uow, me, periode, ct);
        var anomalies = await SelfSpaceQueries.AnomaliesAsync(_uow, me, periode, ct);
        var absences = await AbsencesListAsync(me, periode, ct);
        var retards = await DelaysListAsync(me, periode, ct);
        return new SelfSummaryDto(
            periode,
            semaines.Count,
            semaines.Sum(s => s.HeuresSupplementaires),
            semaines.Sum(s => s.Hs130),
            semaines.Sum(s => s.Hs150),
            anomalies.Count,
            absences.Count(a => a.Type == AbsenceReportRules.TypeNonJustifiee),
            absences.Count(a => a.Type == AbsenceReportRules.TypeCongeSage),
            retards.Count,
            retards.Sum(r => r.MinutesRetard));
    }

    /// <summary>Semaines validées (lundi → dimanche), heures supplémentaires 130 % / 150 % et cumuls EXO /
    /// IMPO.</summary>
    public async Task<SelfWeeksDto> WeeksAsync(SelfPeriodQuery query, CancellationToken ct = default)
    {
        var (me, periode) = await ResoudreAsync(query, ct);
        return new SelfWeeksDto(
            periode,
            await SelfSpaceQueries.SemainesValideesAsync(_uow, me, periode, ct),
            await SelfSpaceQueries.ExoImpoAsync(_uow, me, periode, ct));
    }

    public async Task<SelfAnomaliesDto> AnomaliesAsync(SelfPeriodQuery query, CancellationToken ct = default)
    {
        var (me, periode) = await ResoudreAsync(query, ct);
        return new SelfAnomaliesDto(periode, await SelfSpaceQueries.AnomaliesAsync(_uow, me, periode, ct));
    }

    /// <summary>Absences et congés (congés lus dans SAGE).</summary>
    public async Task<SelfAbsencesDto> AbsencesAsync(SelfPeriodQuery query, CancellationToken ct = default)
    {
        var (me, periode) = await ResoudreAsync(query, ct);
        return new SelfAbsencesDto(periode, await AbsencesListAsync(me, periode, ct));
    }

    public async Task<SelfDelaysDto> DelaysAsync(SelfPeriodQuery query, CancellationToken ct = default)
    {
        var (me, periode) = await ResoudreAsync(query, ct);
        return new SelfDelaysDto(periode, await DelaysListAsync(me, periode, ct));
    }

    private DateTime Aujourdhui() => _horloge.GetLocalNow().Date;

    /// <summary>Salarié connecté (garde) puis période bornée : la garde passe toujours avant la lecture de la
    /// période.</summary>
    private async Task<(SelfContext Me, SelfPeriodDto Periode)> ResoudreAsync(
        SelfPeriodQuery query, CancellationToken ct)
    {
        var me = await _guard.ResolveAsync(ct);
        var (debut, fin) = SelfSpacePeriodRules.Borner(query.From, query.To, Aujourdhui());
        return (me, new SelfPeriodDto(debut, fin));
    }

    private async Task<List<SelfAbsenceDto>> AbsencesListAsync(
        SelfContext me, SelfPeriodDto periode, CancellationToken ct)
    {
        var sources = await SourcesAsync(me, periode, ct);
        var feries = await SelfSpaceQueries.FeriesAsync(_uow, me, ct);
        var calendrier = new AbsenceCalendar(await CongesAsync(me, ct), feries);
        // Les règles reçoivent uniquement les cartes du salarié : ses lignes sont les seules possibles ; le filtre
        // reste explicite.
        var lignes = AbsenceReportRules.Calculer(
            sources, calendrier, new ReportPeriod(periode.From, periode.To, Aujourdhui()));
        return lignes
            .Where(l => SelfSpaceQueries.EstMoi(l.Matricule, me))
            .OrderBy(l => l.Date)
            .Select(l => new SelfAbsenceDto(l.Date.Date, l.Periode, l.Type, l.DureeHeures, l.Motif))
            .ToList();
    }

    private async Task<List<SelfDelayDto>> DelaysListAsync(SelfContext me, SelfPeriodDto periode, CancellationToken ct)
    {
        var sources = await SourcesAsync(me, periode, ct);
        var tolerances = await SelfSpaceQueries.TolerancesAsync(_uow, me, ct);
        var feries = await SelfSpaceQueries.FeriesAsync(_uow, me, ct);
        var lignes = RetardReportRules.Calculer(
            sources, tolerances, feries, new ReportPeriod(periode.From, periode.To, Aujourdhui()));
        return lignes
            .Where(r => SelfSpaceQueries.EstMoi(r.Matricule, me) && r.MinutesRetard > 0)
            .OrderBy(r => r.Date)
            .Select(r => new SelfDelayDto(
                r.Date.Date, r.HeureEntree, r.HeurePrevue, r.ToleranceMinutes, r.MinutesRetard, r.RetardSaisi))
            .ToList();
    }

    /// <summary>Cartes, plannings et corrections du salarié sur la période, plafonnée à la veille comme les
    /// états.</summary>
    private async Task<EmployeeSources> SourcesAsync(SelfContext me, SelfPeriodDto periode, CancellationToken ct)
    {
        var hier = Aujourdhui().AddDays(-1);
        var fenetre = (periode.From, (periode.To < hier ? periode.To : hier).AddDays(1));
        return new EmployeeSources(
            me.Cartes,
            await SelfSpaceQueries.PlanningsAsync(_uow, me, fenetre, ct),
            await SelfSpaceQueries.CorrectionsAsync(_uow, me, fenetre, ct));
    }

    /// <summary>
    /// Congés SAGE du salarié. Le calendrier SAGE lit les congés de tout le tenant (aucune requête filtrée n'existe
    /// dans la source) : une seule lecture est partagée entre tous les salariés de la base (<see
    /// cref="ISelfLeaveCache"/>), qui
    /// n'en rend que la part du salarié connecté. Source SAGE indisponible : les absences sont calculées sans les
    /// congés SAGE, comme dans les états, et l'incident est journalisé.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, List<LeaveDay>>> CongesAsync(SelfContext me, CancellationToken ct)
    {
        try
        {
            var siens = await _cacheConges.ObtenirAsync(me, () => _leaves.LoadAsync(CancellationToken.None), ct);
            return new Dictionary<string, List<LeaveDay>>(StringComparer.Ordinal) { [me.Cle] = siens };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Congés SAGE indisponibles pour Mon espace sur la base {SageDb}.", me.SageDb);
            return new Dictionary<string, List<LeaveDay>>(StringComparer.Ordinal);
        }
    }
}
