using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;

namespace SoftTime.Infrastructure.Services;

/// <summary>
/// Orchestration d'un cycle pour la société active : rafraîchissement de l'ETL (incrémental ou
/// complet par trimestres), entraînement éventuel, puis génération des prédictions.
/// </summary>
public sealed class MlPipelineService : IMlPipelineService
{
    private readonly IMlDatasetService _dataset;
    private readonly IMlTrainingService _training;
    private readonly IMlPredictionService _prediction;
    private readonly ITenantConnectionService _tenant;
    private readonly MlOptions _options;
    private readonly ILogger<MlPipelineService> _logger;

    public MlPipelineService(
        IMlDatasetService dataset,
        IMlTrainingService training,
        IMlPredictionService prediction,
        ITenantConnectionService tenant,
        IOptions<MlOptions> options,
        ILogger<MlPipelineService> logger)
    {
        _dataset = dataset;
        _training = training;
        _prediction = prediction;
        _tenant = tenant;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MlPipelineResultDto> RunAsync(
        bool fullRefresh, bool train, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var to = DateTime.Today.AddDays(-1);
        var dailyRows = 0;
        var weeklyRows = 0;

        if (fullRefresh)
        {
            var from = to.AddMonths(-_options.HistoryMonths);
            foreach (var (start, end) in Chunks(from, to, Math.Max(1, _options.QuarterChunkMonths)))
            {
                var result = await _dataset.BuildAsync(start, end, persist: true, ct);
                dailyRows += result.DailyRows;
                weeklyRows += result.WeeklyRows;
            }
        }
        else
        {
            var from = MondayOf(to.AddDays(-7 * Math.Max(1, _options.RefreshWindowWeeks)));
            var result = await _dataset.BuildAsync(from, to, persist: true, ct);
            dailyRows = result.DailyRows;
            weeklyRows = result.WeeklyRows;
        }

        string? trainingMessage = null;
        if (train)
        {
            var outcome = await _training.TrainAsync(sage, ct);
            trainingMessage = outcome.Message;
        }

        var predictions = await _prediction.GenerateAsync(ct);

        _logger.LogInformation(
            "Pipeline ML {Sage} : {Daily} jours, {Weekly} semaines, {Hs} prédictions HS.",
            sage, dailyRows, weeklyRows, predictions.HsPredictions);

        var message = string.Join(" ", new[] { trainingMessage, predictions.Message }
            .Where(m => !string.IsNullOrWhiteSpace(m)));

        return new MlPipelineResultDto(
            predictions.Etat,
            message,
            dailyRows,
            weeklyRows,
            predictions.HsPredictions);
    }

    /// <summary>
    /// Tranches du backfill alignées sur des semaines complètes (lundi → dimanche) : une semaine à
    /// cheval sur deux tranches serait sinon calculée sur quelques jours puis jamais recalculée.
    /// </summary>
    internal static IEnumerable<(DateTime Start, DateTime End)> Chunks(
        DateTime from, DateTime to, int months)
    {
        var start = MondayOf(from);
        while (start <= to.Date)
        {
            var end = MondayOf(start.AddMonths(months)).AddDays(-1);
            if (end < start)
                end = start.AddDays(6);
            if (end > to.Date)
                end = to.Date;
            yield return (start, end);
            start = end.AddDays(1);
        }
    }

    private static DateTime MondayOf(DateTime date)
        => date.Date.AddDays(-(((int)date.Date.DayOfWeek + 6) % 7));
}
