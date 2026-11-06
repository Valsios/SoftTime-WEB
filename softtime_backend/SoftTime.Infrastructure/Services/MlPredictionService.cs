using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.Ml;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models.Ml;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services;

public sealed class MlPredictionService : IMlPredictionService
{
    private readonly IMlDatasetService _dataset;
    private readonly IMlStore _store;
    private readonly IMlPredictor _predictor;
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly MlOptions _options;
    private readonly ILogger<MlPredictionService> _logger;

    public MlPredictionService(
        IMlDatasetService dataset,
        IMlStore store,
        IMlPredictor predictor,
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IOptions<MlOptions> options,
        ILogger<MlPredictionService> logger)
    {
        _dataset = dataset;
        _store = store;
        _predictor = predictor;
        _uow = uow;
        _tenant = tenant;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Recalcule les prédictions de l'horizon pour la société active.</summary>
    public async Task<MlPipelineResultDto> GenerateAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var window = ForecastWindow(DateTime.Today);

        // Sans pointage récent, les features décrivent un passé lointain : on ne prédit pas, et
        // les anciennes prévisions de l'horizon sont retirées pour ne pas rester affichées.
        if (!await HasRecentPunchesAsync(sage, window.Today, ct))
        {
            await ClearAsync(sage, MlModelType.Absence, window.Today, window.AbsenceTo, ct);
            await ClearHsAsync(sage, window.WeekStart, window.WeekEnd, ct);
            var raison = $"Aucun pointage depuis {_options.RecentDataDays} jours : prévisions suspendues.";
            _logger.LogWarning("Prévisions {Sage} : {Raison}", sage, raison);
            return new MlPipelineResultDto("Baseline", raison, 0, 0, 0);
        }

        await PurgeRetentionAsync(sage, window, ct);
        var (weeklyRows, hsCount) = await PredictHsAsync(sage, window, ct);
        if (hsCount == 0)
            await ClearHsAsync(sage, window.WeekStart, window.WeekEnd, ct);

        _logger.LogInformation("Prévisions {Sage} : {Hs} ligne(s) HS.", sage, hsCount);
        return hsCount > 0
            ? new MlPipelineResultDto("Pret", "Prévisions générées.", 0, weeklyRows, hsCount)
            : new MlPipelineResultDto("Baseline", "Aucun modèle actif : le moteur reste en mode baseline.", 0, weeklyRows, hsCount);
    }

    private MlForecastWindow ForecastWindow(DateTime today)
    {
        var weekStart = MlWeeks.MondayOf(today);
        return new MlForecastWindow(
            today,
            today.AddDays(Math.Max(1, _options.ForecastHorizonDays)),
            weekStart,
            weekStart.AddDays(7 * Math.Max(1, _options.ForecastHorizonWeeks) - 1));
    }

    private async Task<bool> HasRecentPunchesAsync(string sage, DateTime today, CancellationToken ct)
    {
        var depuis = today.AddDays(-Math.Max(1, _options.RecentDataDays));
        var pointagesRecents = await _uow.Repository<HeureCorrigee>().ListReadOnlyAsync(
            h => h.BddSage == sage && h.DatePointageIn >= depuis && h.DatePointageIn < today, ct);
        return pointagesRecents.Count > 0;
    }

    /// <summary>
    /// La prévision d'absence n'est plus produite : toutes ses lignes héritées sont retirées. Les
    /// semaines HS passées restent pour le suivi prévu / réel, dans la limite de l'historique.
    /// </summary>
    private async Task PurgeRetentionAsync(string sage, MlForecastWindow window, CancellationToken ct)
    {
        await ClearAsync(sage, MlModelType.Absence, DebutRetention, window.AbsenceTo, ct);
        var finRetention = MlWeeks.MondayOf(window.Today.AddMonths(-Math.Max(1, _options.HistoryMonths))).AddDays(-1);
        await ClearHsAsync(sage, DebutRetention, finRetention, ct);
    }

    private Task ClearAsync(string sage, MlModelType type, DateTime from, DateTime to, CancellationToken ct) =>
        _store.ReplacePredictionsAsync(sage, type, from, to, Array.Empty<MlPrediction>(), ct);

    private async Task ClearHsAsync(string sage, DateTime from, DateTime to, CancellationToken ct)
    {
        await ClearAsync(sage, MlModelType.HsProbabilite, from, to, ct);
        await ClearAsync(sage, MlModelType.HsMontant, from, to, ct);
    }

    /// <summary>Prédit probabilité et montant HS de chaque semaine de l'horizon ayant de l'activité.</summary>
    private async Task<(int WeeklyRows, int HsCount)> PredictHsAsync(string sage, MlForecastWindow window, CancellationToken ct)
    {
        var hsProbability = Utilisable(await _store.GetActiveModelAsync(sage, MlModelType.HsProbabilite, ct));
        var hsAmount = Utilisable(await _store.GetActiveModelAsync(sage, MlModelType.HsMontant, ct));
        if (hsProbability?.ModeleBinaire is not { Length: > 0 } probabilityBinary
            || hsAmount?.ModeleBinaire is not { Length: > 0 } amountBinary)
            return (0, 0);

        var weeks = (await _dataset.BuildInferenceWeeklyAsync(window.WeekStart, window.WeekEnd, ct))
            .Where(w => w.NbJoursPlanifies > 0 || w.HeuresTravaillees > 0m)
            .ToList();
        if (weeks.Count == 0)
            return (0, 0);

        var samples = weeks.Select(MlSampleMapper.ToHs).ToList();
        var batch = new MlPredictionBatch(sage, DateTime.Now, weeks, _predictor.PredictHsProbability(probabilityBinary, samples));
        var probabilityPredictions = ProbabilityPredictions(batch, hsProbability.Id);
        var amountPredictions = AmountPredictions(batch, hsAmount, _predictor.PredictHsAmount(amountBinary, samples));

        await _store.ReplacePredictionsAsync(sage, MlModelType.HsProbabilite, window.WeekStart, window.WeekEnd, probabilityPredictions, ct);
        await _store.ReplacePredictionsAsync(sage, MlModelType.HsMontant, window.WeekStart, window.WeekEnd, amountPredictions, ct);
        return (weeks.Count, amountPredictions.Count);
    }

    private List<MlPrediction> ProbabilityPredictions(MlPredictionBatch batch, int modelId) =>
        batch.Weeks.Select((week, index) => new MlPrediction
        {
            BddSage = batch.Sage,
            Matricule = week.Matricule,
            TypeModele = MlModelTypes.HsProbabilite,
            Cible = week.SemaineDebut,
            Probabilite = batch.Probabilities[index],
            NiveauRisque = Niveau(batch.Probabilities[index]),
            ModeleId = modelId,
            GenereLe = batch.GenereLe
        }).ToList();

    /// <summary>Demi-largeur de l'intervalle : erreur observée sur le jeu de test (80e centile, sinon MAE).</summary>
    private List<MlPrediction> AmountPredictions(MlPredictionBatch batch, MlModele model, IReadOnlyList<float> amounts)
    {
        var metriques = LireMetriques(model);
        var erreur = (decimal)(metriques?.ErreurP80 ?? metriques?.Mae ?? 0d);
        return batch.Weeks.Select((week, index) =>
        {
            var valeur = Math.Max(0m, Math.Round((decimal)amounts[index], 2));
            return new MlPrediction
            {
                BddSage = batch.Sage,
                Matricule = week.Matricule,
                TypeModele = MlModelTypes.HsMontant,
                Cible = week.SemaineDebut,
                ValeurPrevue = valeur,
                BorneBasse = Math.Round(Math.Max(0m, valeur - erreur), 2),
                BorneHaute = Math.Round(valeur + erreur, 2),
                NiveauRisque = Niveau(batch.Probabilities[index]),
                ModeleId = model.Id,
                GenereLe = batch.GenereLe
            };
        }).ToList();
    }

    // ------------------------------------------------------------------ utilitaires

    /// <summary>Borne basse des purges de prédictions (avant toute donnée possible).</summary>
    private static readonly DateTime DebutRetention = new(2000, 1, 1);

    /// <summary>Un modèle n'est utilisé que s'il a été entraîné sur le contrat de features courant.</summary>
    private MlModele? Utilisable(MlModele? model)
    {
        if (model is null || model.SchemaHash == _predictor.FeatureSchemaHash)
            return model;
        _logger.LogWarning(
            "Modèle {Type} {Version} ignoré : contrat de features obsolète, réentraînement nécessaire.",
            model.TypeModele, model.Version);
        return null;
    }

    private static MlMetrics? LireMetriques(MlModele model)
    {
        try
        {
            return string.IsNullOrWhiteSpace(model.MetriquesJson)
                ? null
                : JsonSerializer.Deserialize<MlMetrics>(model.MetriquesJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private byte Niveau(double probabilite)
        => probabilite >= _options.HighRiskThreshold ? (byte)3
            : probabilite >= _options.LowRiskThreshold ? (byte)2
            : (byte)1;
}
