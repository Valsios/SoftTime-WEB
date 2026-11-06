using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.Ml;
using SoftTime.Domain.Models.Ml;

namespace SoftTime.Infrastructure.Services;

/// <summary>
/// Entraînement des deux modèles d'heures supplémentaires (probabilité et montant) à partir de
/// la base analytique. Le découpage est chronologique :
/// les dernières semaines servent de test, jamais un tirage aléatoire. Un modèle n'est activé que
/// s'il bat la baseline statistique et les seuils minimaux ; sinon l'ancien modèle reste actif et
/// l'état passe explicitement à « Baseline ».
/// </summary>
public sealed class MlTrainingService : IMlTrainingService
{
    private readonly IMlStore _store;
    private readonly IMlTrainer _trainer;
    private readonly MlOptions _options;
    private readonly ILogger<MlTrainingService> _logger;

    public MlTrainingService(
        IMlStore store,
        IMlTrainer trainer,
        IOptions<MlOptions> options,
        ILogger<MlTrainingService> logger)
    {
        _store = store;
        _trainer = trainer;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MlTrainingOutcome> TrainAsync(string bddSage, CancellationToken ct = default)
    {
        var to = DateTime.Today.AddDays(-1);
        // L'entraînement ne peut pas remonter plus loin que l'historique reconstruit.
        var from = to.AddMonths(-Math.Min(_options.TrainingMonths, _options.HistoryMonths));

        var daily = await _store.GetDailyForTrainingAsync(bddSage, from, to, _options.MaxTrainingRows, ct);
        var joursSansPointage = MlDataQuality.JoursSansPointage(
            daily.Select(d => (d.Date, d.EstJourPlanifie, d.AbsenceNonJustifiee)), _options);
        if (joursSansPointage.Count > 0)
            _logger.LogInformation(
                "ML {Sage} : {Count} jour(s) sans pointage collectif écartés de l'entraînement.",
                bddSage, joursSansPointage.Count);

        var hs = await TrainHsAsync(bddSage, joursSansPointage, from, to, ct);
        var etat = hs.HsMontant?.EstActif == true ? "Pret" : "Baseline";

        return new MlTrainingOutcome(etat, hs.Message, hs.HsProbabilite, hs.HsMontant, hs.HsMetrics);
    }

    // ------------------------------------------------------------------ heures supplémentaires

    private async Task<HsOutcome> TrainHsAsync(
        string bddSage, IReadOnlySet<DateTime> joursSansPointage, DateTime from, DateTime to, CancellationToken ct)
    {
        var rows = await LoadHsRowsAsync(bddSage, joursSansPointage, from, to, ct);
        var run = new MlTrainingRun(bddSage, from, to, rows.Count, null);
        if (rows.Count < _options.MinWeeklyRows)
            return await RejectHsAsync(run, $"HS : données insuffisantes ({rows.Count} semaines ; minimum {_options.MinWeeklyRows}).", ct);

        var testStart = to.AddDays(-7 * _options.TestWindowWeeks);
        var train = rows.Where(w => w.SemaineDebut < testStart).Select(MlSampleMapper.ToHs).ToList();
        var test = rows.Where(w => w.SemaineDebut >= testStart).Select(MlSampleMapper.ToHs).ToList();
        var testPositifs = test.Count(s => s.HsPositive);
        if (train.Count == 0 || test.Count == 0 || testPositifs < _options.MinTestPositives)
            return await RejectHsAsync(run, $"HS : jeu de test insuffisant ({test.Count} semaines, {testPositifs} positives).", ct);

        var baselineAuc = Auc(
            test.Select(s => (double)s.HsMoyenne4Semaines).ToList(),
            test.Select(s => s.HsPositive).ToList());
        var baselineMae = test.Average(s => Math.Abs((double)s.HsAmount - s.HsMoyenne4Semaines));
        var probability = ProbabilityCandidate(_trainer.TrainHsProbability(train, test, _options.OvertimeAlgorithm, ct), baselineAuc);
        var amount = AmountCandidate(_trainer.TrainHsAmount(train, test, _options.OvertimeAlgorithm, ct), baselineMae);

        run = run with { Positifs = rows.Count(w => w.Hs > 0) };
        var probModel = await PersistAsync(run, MlModelType.HsProbabilite, probability, ct);
        var amountModel = await PersistAsync(run, MlModelType.HsMontant, amount, ct);

        var (probAuc, amountMae) = (probability.Metrics.Auc ?? 0d, amount.Metrics.Mae ?? double.MaxValue);
        _logger.LogInformation(
            "HS {Sage} : AUC={Auc:F3} (baseline {BaselineAuc:F3}), MAE={Mae:F2} (baseline {BaselineMae:F2}).",
            bddSage, probAuc, baselineAuc, amountMae, baselineMae);
        var message = probability.Accepted && amount.Accepted
            ? $"HS : modèles {probModel.Version} actifs (AUC {probAuc:F3}, MAE {amountMae:F2} vs {baselineMae:F2})."
            : $"HS : {(probability.Accepted ? "probabilité active" : "probabilité en baseline")}, " +
              $"{(amount.Accepted ? "montant actif" : "montant en baseline")}.";
        return new HsOutcome(probModel, amountModel, amount.Metrics, message);
    }

    /// <summary>
    /// Semaines terminées uniquement (la semaine en cours a un label partiel) et sans trou de
    /// pointage (des HS à 0 faute d'import ne sont pas des semaines sans HS).
    /// </summary>
    private async Task<List<MlSalarieSemaine>> LoadHsRowsAsync(
        string bddSage, IReadOnlySet<DateTime> joursSansPointage, DateTime from, DateTime to, CancellationToken ct) =>
        (await _store.GetWeeklyForTrainingAsync(bddSage, from, to, _options.MaxTrainingWeeks, ct))
            .Where(w => w.NbJoursPlanifies > 0 || w.HeuresTravaillees > 0)
            .Where(w => w.SemaineDebut.AddDays(6) <= to)
            .Where(w => !Enumerable.Range(0, 7).Any(i => joursSansPointage.Contains(w.SemaineDebut.Date.AddDays(i))))
            .OrderBy(w => w.SemaineDebut)
            .ToList();

    private async Task<HsOutcome> RejectHsAsync(MlTrainingRun run, string reason, CancellationToken ct)
    {
        _logger.LogInformation("{Reason}", reason);
        await PersistRejectionAsync(run, MlModelType.HsProbabilite, reason, ct);
        await PersistRejectionAsync(run, MlModelType.HsMontant, reason, ct);
        return new HsOutcome(null, null, null, reason);
    }

    /// <summary>Le modèle de probabilité n'est retenu que s'il bat la baseline (moyenne 4 semaines) en AUC.</summary>
    private MlCandidateModel ProbabilityCandidate(MlTrainingResult result, double baselineAuc)
    {
        var auc = result.Metrics.Auc ?? 0d;
        var accepted = result.Accepted
                       && result.Model is { Length: > 0 }
                       && auc >= _options.AbsenceMinAuc
                       && auc >= baselineAuc + _options.AbsenceAucImprovement;
        return new MlCandidateModel(result, result.Metrics with { BaselineAuc = baselineAuc }, accepted,
            accepted ? null : $"P(HS>0) : AUC {auc:F3}, baseline {baselineAuc:F3}.");
    }

    /// <summary>Le modèle de montant n'est retenu que s'il réduit suffisamment la MAE de la baseline.</summary>
    private MlCandidateModel AmountCandidate(MlTrainingResult result, double baselineMae)
    {
        var mae = result.Metrics.Mae ?? double.MaxValue;
        var accepted = result.Accepted
                       && result.Model is { Length: > 0 }
                       && mae <= baselineMae * (1 - _options.OvertimeMaeImprovement);
        return new MlCandidateModel(result, result.Metrics with { BaselineMae = baselineMae }, accepted,
            accepted ? null : $"Montant HS : MAE {mae:F2}, baseline {baselineMae:F2}.");
    }

    // ------------------------------------------------------------------ persistance

    /// <summary>
    /// Trace un entraînement non réalisé (données insuffisantes) pour que l'UI explique le mode
    /// baseline au lieu d'afficher un simple « aucun modèle ». Les lignes rejetées sont purgées
    /// comme les anciens modèles.
    /// </summary>
    private async Task PersistRejectionAsync(MlTrainingRun run, MlModelType type, string reason, CancellationToken ct)
    {
        var model = NewModel(run, type);
        model.Version = $"{DateTime.Now:yyyyMMdd-HHmm}-baseline";
        model.Algorithme = "aucun";
        model.Statut = MlModelStatus.Rejete;
        model.EstActif = false;
        model.MetriquesJson = "{}";
        model.ModeleBinaire = null;
        model.Commentaire = reason;

        await _store.SaveModelAsync(model, ct);
        await _store.PurgeModelsAsync(run.BddSage, type, _options.ModelRetentionPerType, ct);
    }

    private async Task<MlModele> PersistAsync(MlTrainingRun run, MlModelType type, MlCandidateModel candidate, CancellationToken ct)
    {
        if (candidate.Accepted)
            await _store.ArchiveActiveModelsAsync(run.BddSage, type, ct);

        var model = NewModel(run, type);
        model.Version = $"{DateTime.Now:yyyyMMdd-HHmm}-{candidate.Result.Algorithm}";
        model.Algorithme = candidate.Result.Algorithm;
        model.Statut = candidate.Accepted ? MlModelStatus.Active : MlModelStatus.Rejete;
        model.EstActif = candidate.Accepted;
        model.MetriquesJson = JsonSerializer.Serialize(candidate.Metrics);
        model.ModeleBinaire = candidate.Accepted ? candidate.Result.Model : null;
        model.Commentaire = candidate.Commentaire;
        await _store.SaveModelAsync(model, ct);

        if (candidate.Accepted)
            await _store.PurgeModelsAsync(run.BddSage, type, _options.ModelRetentionPerType, ct);
        return model;
    }

    private MlModele NewModel(MlTrainingRun run, MlModelType type) => new()
    {
        BddSage = run.BddSage,
        TypeModele = MlModelTypes.ToStorage(type),
        EntraineLe = DateTime.Now,
        PeriodeDebut = run.From,
        PeriodeFin = run.To,
        NbLignes = run.Rows,
        NbPositifs = run.Positifs,
        SchemaHash = _trainer.FeatureSchemaHash
    };

    // ------------------------------------------------------------------ baseline

    private static double Auc(IReadOnlyList<double> scores, IReadOnlyList<bool> labels)
    {
        var positifs = new List<double>();
        var negatifs = new List<double>();
        for (var i = 0; i < labels.Count; i++)
            (labels[i] ? positifs : negatifs).Add(scores[i]);

        if (positifs.Count == 0 || negatifs.Count == 0)
            return 0.5d;

        double somme = 0d;
        foreach (var p in positifs)
        {
            foreach (var n in negatifs)
                somme += p > n ? 1d : Math.Abs(p - n) < 1e-9 ? 0.5d : 0d;
        }

        return somme / (positifs.Count * negatifs.Count);
    }

    private sealed record HsOutcome(
        MlModele? HsProbabilite, MlModele? HsMontant, MlMetrics? HsMetrics, string Message);
}
