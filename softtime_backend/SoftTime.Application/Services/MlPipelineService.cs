using Microsoft.Extensions.Logging;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Models.Ml;

namespace SoftTime.Application.Services;

/// <summary>
/// Orchestration d'un cycle pour la société active : rafraîchissement de l'ETL (incrémental ou
/// complet par trimestres), entraînement éventuel, puis génération des prédictions.
/// </summary>
public sealed class MlPipelineService
{
    private readonly MlDatasetService _dataset;
    private readonly MlTrainingService _training;
    private readonly MlPredictionService _prediction;
    private readonly TenantConnectionService _tenant;
    private readonly MlOptions _options;
    private readonly ILogger<MlPipelineService> _logger;

    public MlPipelineService(
        MlDatasetService dataset,
        MlTrainingService training,
        MlPredictionService prediction,
        TenantConnectionService tenant,
        MlOptions options,
        ILogger<MlPipelineService> logger)
    {
        _dataset = dataset;
        _training = training;
        _prediction = prediction;
        _tenant = tenant;
        _options = options;
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
            var from = to.AddDays(-7 * Math.Max(1, _options.RefreshWindowWeeks));
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
            "Pipeline ML {Sage} : {Daily} jours, {Weekly} semaines, {Absence} prédictions absence, {Hs} prédictions HS.",
            sage, dailyRows, weeklyRows, predictions.AbsencePredictions, predictions.HsPredictions);

        var message = string.Join(" ", new[] { trainingMessage, predictions.Message }
            .Where(m => !string.IsNullOrWhiteSpace(m)));

        return new MlPipelineResultDto(
            predictions.Etat,
            message,
            dailyRows,
            weeklyRows,
            predictions.AbsencePredictions,
            predictions.HsPredictions);
    }

    private static IEnumerable<(DateTime Start, DateTime End)> Chunks(
        DateTime from, DateTime to, int months)
    {
        var start = from.Date;
        while (start <= to.Date)
        {
            var end = start.AddMonths(months).AddDays(-1);
            if (end > to.Date)
                end = to.Date;
            yield return (start, end);
            start = end.AddDays(1);
        }
    }
}
