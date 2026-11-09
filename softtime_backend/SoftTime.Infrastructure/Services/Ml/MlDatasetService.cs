using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions.Ml;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.Ml;

namespace SoftTime.Infrastructure.Services.Ml;

public sealed class MlDatasetService : IMlDatasetService
{
    private readonly IMlStore _store;
    private readonly MlOptions _options;
    private readonly ILogger<MlDatasetService> _logger;
    private readonly IMlDatasetContextLoader _mlDatasetContextLoader;

    public MlDatasetService(
        IMlStore store,
        IOptions<MlOptions> options,
        ILogger<MlDatasetService> logger,
        IMlDatasetContextLoader mlDatasetContextLoader)
    {
        _store = store;
        _options = options.Value;
        _logger = logger;
        _mlDatasetContextLoader = mlDatasetContextLoader;
    }

    /// <summary>
    /// Reconstruit les lignes jour/semaine de la plage. <paramref name="persist"/> à false permet
    /// de réutiliser exactement le même calcul pour l'inférence (planning futur, sans écriture).
    /// </summary>
    public async Task<MlDatasetBuildResult> BuildAsync(
        DateTime from, DateTime to, bool persist, CancellationToken ct = default)
    {
        from = from.Date;
        to = to.Date;
        if (to < from)
            throw new DomainException("La date de fin doit suivre la date de début.");

        var ctx = await _mlDatasetContextLoader.LoadContextAsync(from, to, ct);
        Prepare(ctx);

        var daily = ctx.EmployeeDays.Values
            .SelectMany(days => days)
            .Where(d => d.Date >= from && d.Date <= to)
            .Select(d => MlDatasetProjection.ToDailyEntity(ctx, d))
            .ToList();

        var weekly = MlDatasetProjection.BuildWeekly(ctx, from, to);

        if (persist)
        {
            await _store.ReplaceDailyAsync(ctx.SageDb, from, to, daily, ct);
            await _store.ReplaceWeeklyAsync(ctx.SageDb, from, to, weekly, ct);
        }

        _logger.LogInformation(
            "ML dataset {Sage} : {Daily} jour(s), {Weekly} semaine(s) ({From:yyyy-MM-dd} → {To:yyyy-MM-dd}, persist={Persist}).",
            ctx.SageDb, daily.Count, weekly.Count, from, to, persist);

        return new MlDatasetBuildResult(daily.Count, weekly.Count, from, to);
    }

    /// <summary>Lignes d'inférence (planning futur) réutilisées par <see cref="MlPredictionService"/>.</summary>
    public async Task<IReadOnlyList<MlSalarieJour>> BuildInferenceDailyAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        var ctx = await _mlDatasetContextLoader.LoadContextAsync(from.Date, to.Date, ct);
        Prepare(ctx);
        return ctx.EmployeeDays.Values
            .SelectMany(days => days)
            .Where(d => d.Date >= from.Date && d.Date <= to.Date && d.EstJourPlanifie)
            .OrderBy(d => d.Matricule).ThenBy(d => d.Date)
            .Select(d => MlDatasetProjection.ToDailyEntity(ctx, d))
            .ToList();
    }

    /// <summary>Lignes d'inférence hebdomadaires (semaines futures).</summary>
    public async Task<IReadOnlyList<MlSalarieSemaine>> BuildInferenceWeeklyAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        var ctx = await _mlDatasetContextLoader.LoadContextAsync(from.Date, to.Date, ct);
        Prepare(ctx);
        return MlDatasetProjection.BuildWeekly(ctx, from.Date, to.Date);
    }

    /// <summary>
    /// Journées, trous de données puis features. Les jours sans pointage collectif (import ou
    /// traitement non fait) sont écartés des historiques pour ne pas être lus comme des absences.
    /// </summary>
    private void Prepare(DatasetContext ctx)
    {
        MlDayFeatures.BuildDays(ctx);
        ctx.JoursSansPointage = MlDataQuality.JoursSansPointage(
            ctx.EmployeeDays.Values
                .SelectMany(days => days)
                .Where(d => d.Date < ctx.ObservedUntil)
                .Select(d => (d.Date, d.EstJourPlanifie, d.AbsenceNonJustifiee)),
            _options);
        MlDayFeatures.ComputeRollingFeatures(ctx);
        MlDayFeatures.ComputeTeamFeatures(ctx);
    }
}
