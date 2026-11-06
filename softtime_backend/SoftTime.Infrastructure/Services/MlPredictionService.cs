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
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

/// <summary>
/// Génération et lecture des prédictions. Les lignes sont précalculées par lot (le scheduler) ;
/// les lectures HTTP ne font que filtrer <c>ml.Prediction</c>, ce qui garde l'API rapide et
/// indépendante du chargement des modèles.
/// </summary>
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
        var weekStart = MondayOf(today);
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
        var finRetention = MondayOf(today.AddMonths(-Math.Max(1, _options.HistoryMonths))).AddDays(-1);
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

    // ------------------------------------------------------------------ lecture

    public async Task<MlStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        // Les modèles d'absence hérités restent en base mais ne font plus partie du moteur.
        var models = (await _store.ListModelsAsync(sage, ct))
            .Where(m => m.TypeModele != MlModelTypes.Absence)
            .ToList();
        var active = models.Where(m => m.EstActif && m.SchemaHash == _predictor.FeatureSchemaHash).ToList();
        var obsoletes = models.Any(m => m.EstActif && m.SchemaHash != _predictor.FeatureSchemaHash);

        var etat = !_options.Enabled
            ? "Desactive"
            : active.Count > 0 ? "Pret" : "Baseline";
        var message = etat switch
        {
            "Desactive" => "Moteur prédictif désactivé par la configuration.",
            "Baseline" when obsoletes => "Mode baseline : les modèles actifs utilisent un ancien contrat de features, un réentraînement est nécessaire.",
            "Baseline" => BaselineMessage(models),
            _ => $"{active.Count} modèle(s) actif(s)."
        };

        DateTime? derniereGeneration = null;
        var weekStart = MondayOf(DateTime.Today);
        var predictions = await _store.GetPredictionsAsync(
            sage, MlModelType.HsMontant, weekStart, weekStart.AddDays(7 * Math.Max(1, _options.ForecastHorizonWeeks) - 1), ct);
        if (predictions.Count > 0)
            derniereGeneration = predictions.Max(p => p.GenereLe);

        return new MlStatusDto(
            _options.Enabled,
            etat,
            message,
            derniereGeneration,
            OvertimeRules.ExemptCeilingHours,
            models.Select(ToDto).ToList());
    }

    public async Task<IReadOnlyList<MlHsForecastDto>> GetHsForecastsAsync(
        DateTime semaine, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var debut = MondayOf(semaine);
        var fin = debut.AddDays(6);

        var probabilites = (await _store.GetPredictionsAsync(sage, MlModelType.HsProbabilite, debut, fin, ct))
            .Where(p => p.Cible == debut)
            .ToDictionary(p => p.Matricule, StringComparer.Ordinal);
        var montants = (await _store.GetPredictionsAsync(sage, MlModelType.HsMontant, debut, fin, ct))
            .Where(p => p.Cible == debut)
            .ToDictionary(p => p.Matricule, StringComparer.Ordinal);

        if (montants.Count == 0)
            return Array.Empty<MlHsForecastDto>();

        var cards = await LoadCardsAsync(sage, ct);
        var resultat = new List<MlHsForecastDto>();

        foreach (var (matricule, montant) in montants)
        {
            cards.TryGetValue(matricule, out var card);
            probabilites.TryGetValue(matricule, out var probabilite);
            resultat.Add(new MlHsForecastDto(
                matricule,
                card?.SageNom,
                card?.SagePrenom,
                card?.Departement,
                debut,
                montant.ValeurPrevue ?? 0m,
                montant.BorneBasse ?? 0m,
                montant.BorneHaute ?? 0m,
                probabilite?.Probabilite ?? 0d,
                probabilite?.NiveauRisque ?? 1));
        }

        return resultat.OrderByDescending(r => r.HeuresPrevues).ToList();
    }

    /// <summary>
    /// Totaux HS par semaine sur l'horizon (semaine en cours comprise). Les semaines sans
    /// prévision (planning pas encore saisi) sont renvoyées à zéro pour que l'écran l'explique.
    /// </summary>
    public async Task<IReadOnlyList<MlHsWeekDto>> GetHsHorizonAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var debut = MondayOf(DateTime.Today);
        var semaines = Math.Max(1, _options.ForecastHorizonWeeks);
        var fin = debut.AddDays(7 * semaines - 1);

        var montants = await _store.GetPredictionsAsync(sage, MlModelType.HsMontant, debut, fin, ct);

        return Enumerable.Range(0, semaines)
            .Select(i =>
            {
                var lundi = debut.AddDays(7 * i);
                var lignes = montants.Where(p => p.Cible.Date == lundi).ToList();
                return new MlHsWeekDto(
                    lundi,
                    lignes.Count,
                    lignes.Count(p => (p.ValeurPrevue ?? 0m) > OvertimeRules.ExemptCeilingHours),
                    lignes.Sum(p => p.ValeurPrevue ?? 0m),
                    lignes.Sum(p => p.BorneBasse ?? 0m),
                    lignes.Sum(p => p.BorneHaute ?? 0m));
            })
            .ToList();
    }

    /// <summary>
    /// Prévu / réel des dernières semaines terminées : la dernière prévision émise pour chaque
    /// salarié est comparée aux HS de la base analytique (validées, sinon calculées). Seuls les
    /// salariés présents des deux côtés sont comparés.
    /// </summary>
    public async Task<IReadOnlyList<MlHsAccuracyDto>> GetHsAccuracyAsync(int semaines, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var finDerniere = MondayOf(DateTime.Today).AddDays(-1);
        var debut = MondayOf(DateTime.Today).AddDays(-7 * Math.Clamp(semaines, 1, 26));

        var montants = await _store.GetPredictionsAsync(sage, MlModelType.HsMontant, debut, finDerniere, ct);
        if (montants.Count == 0)
            return Array.Empty<MlHsAccuracyDto>();

        var reels = (await _store.GetWeeklyAsync(sage, debut, finDerniere, ct))
            .ToDictionary(w => (w.Matricule, w.SemaineDebut.Date), w => w.Hs);

        var resultat = new List<MlHsAccuracyDto>();
        foreach (var semaine in montants.GroupBy(p => p.Cible.Date).OrderByDescending(g => g.Key))
        {
            var paires = semaine
                .Where(p => reels.ContainsKey((p.Matricule, semaine.Key)))
                .Select(p => (Prevision: p, Reel: reels[(p.Matricule, semaine.Key)]))
                .ToList();
            if (paires.Count == 0)
                continue;

            var prevu = paires.Sum(x => x.Prevision.ValeurPrevue ?? 0m);
            var reel = paires.Sum(x => x.Reel);
            var ecartMoyen = paires.Average(x => Math.Abs((x.Prevision.ValeurPrevue ?? 0m) - x.Reel));
            var dansFourchette = paires.Count(x =>
                x.Reel >= (x.Prevision.BorneBasse ?? 0m) && x.Reel <= (x.Prevision.BorneHaute ?? 0m));

            resultat.Add(new MlHsAccuracyDto(
                semaine.Key,
                paires.Count,
                Math.Round(prevu, 2),
                Math.Round(reel, 2),
                Math.Round(ecartMoyen, 2),
                Math.Round((double)dansFourchette / paires.Count, 4)));
        }

        return resultat;
    }

    // ------------------------------------------------------------------ utilitaires

    /// <summary>Borne basse des purges de prédictions (avant toute donnée possible).</summary>
    private static readonly DateTime DebutRetention = new(2000, 1, 1);

    private async Task<Dictionary<string, CardPaie>> LoadCardsAsync(string sage, CancellationToken ct)
    {
        var cards = await _uow.Repository<CardPaie>().ListAsync(c => c.SageBdd == sage, ct);
        return cards
            .Where(c => !string.IsNullOrWhiteSpace(c.SageMatricule))
            .GroupBy(c => MatriculeIdentity.Cle(c.SageMatricule))
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Id).First(), StringComparer.Ordinal);
    }

    private static string BaselineMessage(IReadOnlyList<MlModele> models)
    {
        var dernier = models
            .Where(m => m.Statut == MlModelStatus.Rejete && !string.IsNullOrWhiteSpace(m.Commentaire))
            .OrderByDescending(m => m.EntraineLe)
            .FirstOrDefault();

        return dernier?.Commentaire is { Length: > 0 } raison
            ? $"Mode baseline : {raison}"
            : "Aucun modèle actif : données insuffisantes ou modèle indisponible.";
    }

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

    private static DateTime MondayOf(DateTime date)
        => date.Date.AddDays(-(((int)date.Date.DayOfWeek + 6) % 7));

    private static MlModelDto ToDto(MlModele model) => new(
        model.Id,
        model.TypeModele,
        model.Version,
        model.Algorithme,
        model.Statut,
        model.EstActif,
        model.EntraineLe,
        model.PeriodeDebut,
        model.PeriodeFin,
        model.NbLignes,
        model.NbPositifs,
        model.MetriquesJson,
        model.Commentaire);
}
