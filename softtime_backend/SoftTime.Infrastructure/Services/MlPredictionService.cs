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
        var today = DateTime.Today;
        var now = DateTime.Now;
        var absenceTo = today.AddDays(Math.Max(1, _options.ForecastHorizonDays));
        var weekStart = MlWeeks.MondayOf(today);
        var weekEnd = weekStart.AddDays(7 * Math.Max(1, _options.ForecastHorizonWeeks) - 1);

        // Sans pointage récent, les features décrivent un passé lointain : on ne prédit pas, et
        // les anciennes prévisions de l'horizon sont retirées pour ne pas rester affichées.
        var depuis = today.AddDays(-Math.Max(1, _options.RecentDataDays));
        var pointagesRecents = (await _uow.Repository<HeureCorrigee>().ListReadOnlyAsync(
                h => h.BddSage == sage && h.DatePointageIn >= depuis && h.DatePointageIn < today, ct))
            .Count;
        if (pointagesRecents == 0)
        {
            await _store.ReplacePredictionsAsync(sage, MlModelType.Absence, today, absenceTo, Array.Empty<MlPrediction>(), ct);
            await _store.ReplacePredictionsAsync(sage, MlModelType.HsProbabilite, weekStart, weekEnd, Array.Empty<MlPrediction>(), ct);
            await _store.ReplacePredictionsAsync(sage, MlModelType.HsMontant, weekStart, weekEnd, Array.Empty<MlPrediction>(), ct);
            var raison = $"Aucun pointage depuis {_options.RecentDataDays} jours : prévisions suspendues.";
            _logger.LogWarning("Prévisions {Sage} : {Raison}", sage, raison);
            return new MlPipelineResultDto("Baseline", raison, 0, 0, 0);
        }

        // La prévision d'absence n'est plus produite : on retire toutes les lignes héritées.
        await _store.ReplacePredictionsAsync(sage, MlModelType.Absence, DebutRetention, absenceTo, Array.Empty<MlPrediction>(), ct);

        // Les semaines passées restent pour le suivi prévu / réel, dans la limite de l'historique.
        var finRetention = MlWeeks.MondayOf(today.AddMonths(-Math.Max(1, _options.HistoryMonths))).AddDays(-1);
        await _store.ReplacePredictionsAsync(sage, MlModelType.HsProbabilite, DebutRetention, finRetention, Array.Empty<MlPrediction>(), ct);
        await _store.ReplacePredictionsAsync(sage, MlModelType.HsMontant, DebutRetention, finRetention, Array.Empty<MlPrediction>(), ct);

        var hsCount = 0;
        var weeklyRows = 0;
        var hsProbability = Utilisable(await _store.GetActiveModelAsync(sage, MlModelType.HsProbabilite, ct));
        var hsAmount = Utilisable(await _store.GetActiveModelAsync(sage, MlModelType.HsMontant, ct));

        if (hsProbability?.ModeleBinaire is { Length: > 0 } probabilityBinary
            && hsAmount?.ModeleBinaire is { Length: > 0 } amountBinary)
        {
            // Demi-largeur de l'intervalle : erreur observée sur le jeu de test (80e centile, sinon MAE).
            var metriques = LireMetriques(hsAmount);
            var erreur = (decimal)(metriques?.ErreurP80 ?? metriques?.Mae ?? 0d);
            var weeks = (await _dataset.BuildInferenceWeeklyAsync(weekStart, weekEnd, ct))
                .Where(w => w.NbJoursPlanifies > 0 || w.HeuresTravaillees > 0m)
                .ToList();
            weeklyRows = weeks.Count;

            if (weeks.Count > 0)
            {
                var samples = weeks.Select(MlSampleMapper.ToHs).ToList();
                var probabilities = _predictor.PredictHsProbability(probabilityBinary, samples);
                var amounts = _predictor.PredictHsAmount(amountBinary, samples);

                var probabilityPredictions = weeks.Select((week, index) => new MlPrediction
                {
                    BddSage = sage,
                    Matricule = week.Matricule,
                    TypeModele = MlModelTypes.HsProbabilite,
                    Cible = week.SemaineDebut,
                    Probabilite = probabilities[index],
                    NiveauRisque = Niveau(probabilities[index]),
                    ModeleId = hsProbability.Id,
                    GenereLe = now
                }).ToList();

                var amountPredictions = weeks.Select((week, index) =>
                {
                    var valeur = Math.Max(0m, Math.Round((decimal)amounts[index], 2));
                    return new MlPrediction
                    {
                        BddSage = sage,
                        Matricule = week.Matricule,
                        TypeModele = MlModelTypes.HsMontant,
                        Cible = week.SemaineDebut,
                        ValeurPrevue = valeur,
                        BorneBasse = Math.Round(Math.Max(0m, valeur - erreur), 2),
                        BorneHaute = Math.Round(valeur + erreur, 2),
                        NiveauRisque = Niveau(probabilities[index]),
                        ModeleId = hsAmount.Id,
                        GenereLe = now
                    };
                }).ToList();

                await _store.ReplacePredictionsAsync(sage, MlModelType.HsProbabilite, weekStart, weekEnd, probabilityPredictions, ct);
                await _store.ReplacePredictionsAsync(sage, MlModelType.HsMontant, weekStart, weekEnd, amountPredictions, ct);
                hsCount = amountPredictions.Count;
            }
        }
        if (hsCount == 0)
        {
            await _store.ReplacePredictionsAsync(sage, MlModelType.HsProbabilite, weekStart, weekEnd, Array.Empty<MlPrediction>(), ct);
            await _store.ReplacePredictionsAsync(sage, MlModelType.HsMontant, weekStart, weekEnd, Array.Empty<MlPrediction>(), ct);
        }

        _logger.LogInformation("Prévisions {Sage} : {Hs} ligne(s) HS.", sage, hsCount);

        var etat = hsCount > 0 ? "Pret" : "Baseline";
        var message = etat == "Pret"
            ? "Prévisions générées."
            : "Aucun modèle actif : le moteur reste en mode baseline.";

        return new MlPipelineResultDto(etat, message, 0, weeklyRows, hsCount);
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
