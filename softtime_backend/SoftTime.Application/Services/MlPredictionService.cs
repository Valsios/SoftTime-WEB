using Microsoft.Extensions.Logging;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.Ml;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models.Ml;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

/// <summary>
/// Génération et lecture des prédictions. Les lignes sont précalculées par lot (le scheduler) ;
/// les lectures HTTP ne font que filtrer <c>ml.Prediction</c>, ce qui garde l'API rapide et
/// indépendante du chargement des modèles.
/// </summary>
public sealed class MlPredictionService
{
    private readonly MlDatasetService _dataset;
    private readonly IMlStore _store;
    private readonly IMlPredictor _predictor;
    private readonly IUnitOfWork _uow;
    private readonly TenantConnectionService _tenant;
    private readonly MlOptions _options;
    private readonly ILogger<MlPredictionService> _logger;

    public MlPredictionService(
        MlDatasetService dataset,
        IMlStore store,
        IMlPredictor predictor,
        IUnitOfWork uow,
        TenantConnectionService tenant,
        MlOptions options,
        ILogger<MlPredictionService> logger)
    {
        _dataset = dataset;
        _store = store;
        _predictor = predictor;
        _uow = uow;
        _tenant = tenant;
        _options = options;
        _logger = logger;
    }

    /// <summary>Recalcule les prédictions de l'horizon pour la société active.</summary>
    public async Task<MlPipelineResultDto> GenerateAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var today = DateTime.Today;
        var now = DateTime.Now;

        var absenceCount = 0;
        var dailyRows = 0;
        var absenceModel = await _store.GetActiveModelAsync(sage, MlModelType.Absence, ct);

        if (absenceModel?.ModeleBinaire is { Length: > 0 } absenceBinary)
        {
            var to = today.AddDays(Math.Max(1, _options.ForecastHorizonDays));
            var rows = await _dataset.BuildInferenceDailyAsync(today, to, ct);
            dailyRows = rows.Count;

            if (rows.Count > 0)
            {
                var samples = rows.Select(MlSampleMapper.ToAbsence).ToList();
                var probabilities = _predictor.PredictAbsence(absenceBinary, samples);
                var predictions = rows.Select((row, index) => new T_ML_PREDICTION
                {
                    BddSage = sage,
                    Matricule = row.Matricule,
                    TypeModele = MlModelTypes.Absence,
                    Cible = row.Date,
                    Probabilite = probabilities[index],
                    NiveauRisque = Niveau(probabilities[index]),
                    ModeleId = absenceModel.Id,
                    GenereLe = now
                }).ToList();

                await _store.ReplacePredictionsAsync(sage, MlModelType.Absence, today, to, absenceModel.Id, predictions, ct);
                absenceCount = predictions.Count;
            }
        }

        var hsCount = 0;
        var weeklyRows = 0;
        var hsProbability = await _store.GetActiveModelAsync(sage, MlModelType.HsProbabilite, ct);
        var hsAmount = await _store.GetActiveModelAsync(sage, MlModelType.HsMontant, ct);

        if (hsProbability?.ModeleBinaire is { Length: > 0 } probabilityBinary
            && hsAmount?.ModeleBinaire is { Length: > 0 } amountBinary)
        {
            var weekStart = MondayOf(today);
            var weekEnd = weekStart.AddDays(7 * Math.Max(1, _options.ForecastHorizonWeeks) - 1);
            var weeks = (await _dataset.BuildInferenceWeeklyAsync(weekStart, weekEnd, ct))
                .Where(w => w.NbJoursPlanifies > 0 || w.HeuresTravaillees > 0m)
                .ToList();
            weeklyRows = weeks.Count;

            if (weeks.Count > 0)
            {
                var samples = weeks.Select(MlSampleMapper.ToHs).ToList();
                var probabilities = _predictor.PredictHsProbability(probabilityBinary, samples);
                var amounts = _predictor.PredictHsAmount(amountBinary, samples);

                var probabilityPredictions = weeks.Select((week, index) => new T_ML_PREDICTION
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
                    return new T_ML_PREDICTION
                    {
                        BddSage = sage,
                        Matricule = week.Matricule,
                        TypeModele = MlModelTypes.HsMontant,
                        Cible = week.SemaineDebut,
                        ValeurPrevue = valeur,
                        BorneBasse = Math.Round(valeur * 0.8m, 2),
                        BorneHaute = Math.Round(valeur * 1.2m, 2),
                        NiveauRisque = Niveau(probabilities[index]),
                        ModeleId = hsAmount.Id,
                        GenereLe = now
                    };
                }).ToList();

                await _store.ReplacePredictionsAsync(sage, MlModelType.HsProbabilite, weekStart, weekEnd, hsProbability.Id, probabilityPredictions, ct);
                await _store.ReplacePredictionsAsync(sage, MlModelType.HsMontant, weekStart, weekEnd, hsAmount.Id, amountPredictions, ct);
                hsCount = amountPredictions.Count;
            }
        }

        _logger.LogInformation(
            "Prévisions {Sage} : {Absence} ligne(s) absence, {Hs} ligne(s) HS.",
            sage, absenceCount, hsCount);

        var etat = absenceCount > 0 || hsCount > 0 ? "Pret" : "Baseline";
        var message = etat == "Pret"
            ? "Prévisions générées."
            : "Aucun modèle actif : le moteur reste en mode baseline.";

        return new MlPipelineResultDto(etat, message, dailyRows, weeklyRows, absenceCount, hsCount);
    }

    // ------------------------------------------------------------------ lecture

    public async Task<MlStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var models = await _store.ListModelsAsync(sage, ct);
        var active = models.Where(m => m.EstActif).ToList();

        var etat = !_options.Enabled
            ? "Desactive"
            : active.Count > 0 ? "Pret" : "Baseline";
        var message = etat switch
        {
            "Desactive" => "Moteur prédictif désactivé par la configuration.",
            "Baseline" => BaselineMessage(models),
            _ => $"{active.Count} modèle(s) actif(s)."
        };

        DateTime? derniereGeneration = null;
        var today = DateTime.Today;
        var predictions = await _store.GetPredictionsAsync(
            sage, MlModelType.Absence, today, today.AddDays(Math.Max(1, _options.ForecastHorizonDays)), ct);
        if (predictions.Count > 0)
            derniereGeneration = predictions.Max(p => p.GenereLe);

        return new MlStatusDto(
            _options.Enabled,
            etat,
            message,
            derniereGeneration,
            models.Select(ToDto).ToList());
    }

    public async Task<IReadOnlyList<MlAbsenceForecastDto>> GetAbsenceForecastsAsync(
        DateTime from, DateTime to, string? departement, int? categorie, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var predictions = await _store.GetPredictionsAsync(sage, MlModelType.Absence, from, to, ct);
        if (predictions.Count == 0)
            return Array.Empty<MlAbsenceForecastDto>();

        var cards = await LoadCardsAsync(sage, ct);
        var categoriesParCarte = await LoadCategoriesAsync(sage, ct);

        var resultat = new List<MlAbsenceForecastDto>();
        foreach (var groupe in predictions.GroupBy(p => p.Matricule, StringComparer.Ordinal))
        {
            cards.TryGetValue(groupe.Key, out var card);
            if (!string.IsNullOrWhiteSpace(departement)
                && !string.Equals(card?.DEPARTEMENT, departement, StringComparison.OrdinalIgnoreCase))
                continue;

            if (categorie is int idCategorie
                && (!categoriesParCarte.TryGetValue(card?.ID ?? 0, out var carteCategorie)
                    || carteCategorie != idCategorie))
                continue;

            var jours = groupe
                .OrderBy(p => p.Cible)
                .Select(p => new MlAbsenceDayDto(p.Cible, p.Probabilite ?? 0d, p.NiveauRisque, PlanningConnu: true))
                .ToList();

            var risque = 1d;
            foreach (var jour in jours)
                risque *= 1d - Math.Clamp(jour.Probabilite, 0d, 1d);
            risque = 1d - risque;

            var cle = groupe.Key;
            resultat.Add(new MlAbsenceForecastDto(
                cle,
                card?.SAGE_NOM,
                card?.SAGE_PRENOM,
                card?.DEPARTEMENT,
                Math.Round(risque, 4),
                Niveau(risque),
                jours));
        }

        return resultat.OrderByDescending(r => r.Risque).ToList();
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
                card?.SAGE_NOM,
                card?.SAGE_PRENOM,
                card?.DEPARTEMENT,
                debut,
                montant.ValeurPrevue ?? 0m,
                montant.BorneBasse ?? 0m,
                montant.BorneHaute ?? 0m,
                probabilite?.Probabilite ?? 0d,
                probabilite?.NiveauRisque ?? 1));
        }

        return resultat.OrderByDescending(r => r.HeuresPrevues).ToList();
    }

    // ------------------------------------------------------------------ utilitaires

    private async Task<Dictionary<string, T_CARDPAIE>> LoadCardsAsync(string sage, CancellationToken ct)
    {
        var cards = await _uow.Repository<T_CARDPAIE>().ListAsync(c => c.SAGE_BDD == sage, ct);
        return cards
            .Where(c => !string.IsNullOrWhiteSpace(c.SAGE_MATRICULE))
            .GroupBy(c => MatriculeIdentity.Cle(c.SAGE_MATRICULE))
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.ID).First(), StringComparer.Ordinal);
    }

    private async Task<Dictionary<int, int>> LoadCategoriesAsync(string sage, CancellationToken ct)
    {
        var affectations = await _uow.Repository<T_CAT_SAL>().ListAsync(a => a.BDD_SAGE == sage, ct);
        return affectations
            .GroupBy(a => a.MATRICULE_SAGE)
            .ToDictionary(g => g.Key, g => g.First().ID_CATEG);
    }

    private static string BaselineMessage(IReadOnlyList<T_ML_MODELE> models)
    {
        var dernier = models
            .Where(m => m.Statut == MlModelStatus.Rejete && !string.IsNullOrWhiteSpace(m.Commentaire))
            .OrderByDescending(m => m.EntraineLe)
            .FirstOrDefault();

        return dernier?.Commentaire is { Length: > 0 } raison
            ? $"Mode baseline : {raison}"
            : "Aucun modèle actif : données insuffisantes ou modèle indisponible.";
    }

    private byte Niveau(double probabilite)
        => probabilite >= _options.HighRiskThreshold ? (byte)3
            : probabilite >= _options.LowRiskThreshold ? (byte)2
            : (byte)1;

    private static DateTime MondayOf(DateTime date)
        => date.Date.AddDays(-(((int)date.Date.DayOfWeek + 6) % 7));

    private static MlModelDto ToDto(T_ML_MODELE model) => new(
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
