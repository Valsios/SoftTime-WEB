using System.Text.Json;
using Microsoft.Extensions.Logging;
using SoftTime.Application.Abstractions;
using SoftTime.Domain.Entities.Ml;
using SoftTime.Domain.Models.Ml;

namespace SoftTime.Application.Services;

/// <summary>Résultat d'un cycle d'entraînement pour la société active.</summary>
public sealed record MlTrainingOutcome(
    string Etat,
    string Message,
    T_ML_MODELE? Absence,
    T_ML_MODELE? HsProbabilite,
    T_ML_MODELE? HsMontant,
    MlMetrics? AbsenceMetrics,
    MlMetrics? HsMetrics);

/// <summary>
/// Entraînement des trois modèles à partir de la base analytique. Le découpage est chronologique :
/// les dernières semaines servent de test, jamais un tirage aléatoire. Un modèle n'est activé que
/// s'il bat la baseline statistique et les seuils minimaux ; sinon l'ancien modèle reste actif et
/// l'état passe explicitement à « Baseline ».
/// </summary>
public sealed class MlTrainingService
{
    private readonly IMlStore _store;
    private readonly IMlTrainer _trainer;
    private readonly MlOptions _options;
    private readonly ILogger<MlTrainingService> _logger;

    public MlTrainingService(
        IMlStore store,
        IMlTrainer trainer,
        MlOptions options,
        ILogger<MlTrainingService> logger)
    {
        _store = store;
        _trainer = trainer;
        _options = options;
        _logger = logger;
    }

    public async Task<MlTrainingOutcome> TrainAsync(string bddSage, CancellationToken ct = default)
    {
        var to = DateTime.Today.AddDays(-1);
        var from = to.AddMonths(-_options.TrainingMonths);

        var absence = await TrainAbsenceAsync(bddSage, from, to, ct);
        var hs = await TrainHsAsync(bddSage, from, to, ct);

        var etat = absence.Absence?.EstActif == true || hs.HsMontant?.EstActif == true
            ? "Pret"
            : "Baseline";
        var message = string.Join(" ", new[] { absence.Message, hs.Message }.Where(m => !string.IsNullOrWhiteSpace(m)));

        return new MlTrainingOutcome(
            etat,
            message,
            absence.Absence,
            hs.HsProbabilite,
            hs.HsMontant,
            absence.AbsenceMetrics,
            hs.HsMetrics);
    }

    // ------------------------------------------------------------------ absence

    private async Task<AbsenceOutcome> TrainAbsenceAsync(
        string bddSage, DateTime from, DateTime to, CancellationToken ct)
    {
        var rows = (await _store.GetDailyForTrainingAsync(
                bddSage, from, to, _options.MaxTrainingRows, ct))
            .Where(d => d.EstJourPlanifie)
            .OrderBy(d => d.Date)
            .ToList();

        var positifs = rows.Count(d => d.AbsenceNonJustifiee);
        if (rows.Count < _options.MinDailyRows || positifs < _options.MinAbsencePositives)
        {
            var reason = $"Absence : données insuffisantes ({rows.Count} jours planifiés, {positifs} absences ; " +
                         $"minimum {_options.MinDailyRows}/{_options.MinAbsencePositives}).";
            _logger.LogInformation("{Reason}", reason);
            await PersistRejectionAsync(bddSage, MlModelType.Absence, reason, rows.Count, positifs, from, to, ct);
            return new AbsenceOutcome(null, null, reason);
        }

        var testStart = to.AddDays(-7 * _options.TestWindowWeeks);
        var train = rows.Where(d => d.Date < testStart).Select(MlSampleMapper.ToAbsence).ToList();
        var test = rows.Where(d => d.Date >= testStart).Select(MlSampleMapper.ToAbsence).ToList();
        var testPositifs = test.Count(s => s.Label);

        if (train.Count == 0 || test.Count == 0 || testPositifs < _options.MinTestPositives)
        {
            var reason = $"Absence : jeu de test insuffisant ({test.Count} lignes, {testPositifs} positifs).";
            _logger.LogInformation("{Reason}", reason);
            await PersistRejectionAsync(bddSage, MlModelType.Absence, reason, rows.Count, positifs, from, to, ct);
            return new AbsenceOutcome(null, null, reason);
        }

        var baselineAuc = Auc(test.Select(s => (double)s.TauxAbsence30j).ToList(), test.Select(s => s.Label).ToList());
        var result = _trainer.TrainAbsence(train, test, _options.AbsenceAlgorithm, ct);

        var auc = result.Metrics.Auc ?? 0d;
        var accepted = result.Accepted
                       && result.Model is { Length: > 0 }
                       && auc >= _options.AbsenceMinAuc
                       && auc >= baselineAuc + _options.AbsenceAucImprovement;

        var metrics = result.Metrics with { BaselineAuc = baselineAuc };
        var reasonText = accepted
            ? null
            : $"AUC {auc:F3} < max({_options.AbsenceMinAuc:F2}, baseline {baselineAuc:F3} + {_options.AbsenceAucImprovement:F2}).";

        var model = await PersistAsync(
            bddSage, MlModelType.Absence, result, metrics, from, to,
            rows.Count, positifs, accepted, reasonText, ct);

        _logger.LogInformation(
            "Absence {Sage} : AUC={Auc:F3}, baseline={Baseline:F3}, accepté={Accepted}.",
            bddSage, auc, baselineAuc, accepted);

        return new AbsenceOutcome(
            model,
            metrics,
            accepted
                ? $"Absence : modèle {model.Version} actif (AUC {auc:F3} vs baseline {baselineAuc:F3})."
                : reasonText ?? "Absence : baseline conservée.");
    }

    // ------------------------------------------------------------------ heures supplémentaires

    private async Task<HsOutcome> TrainHsAsync(
        string bddSage, DateTime from, DateTime to, CancellationToken ct)
    {
        var rows = (await _store.GetWeeklyForTrainingAsync(
                bddSage, from, to, _options.MaxTrainingWeeks, ct))
            .Where(w => w.NbJoursPlanifies > 0 || w.HeuresTravaillees > 0)
            .OrderBy(w => w.SemaineDebut)
            .ToList();

        if (rows.Count < _options.MinWeeklyRows)
        {
            var reason = $"HS : données insuffisantes ({rows.Count} semaines ; minimum {_options.MinWeeklyRows}).";
            _logger.LogInformation("{Reason}", reason);
            await PersistRejectionAsync(bddSage, MlModelType.HsProbabilite, reason, rows.Count, null, from, to, ct);
            await PersistRejectionAsync(bddSage, MlModelType.HsMontant, reason, rows.Count, null, from, to, ct);
            return new HsOutcome(null, null, null, reason);
        }

        var testStart = to.AddDays(-7 * _options.TestWindowWeeks);
        var train = rows.Where(w => w.SemaineDebut < testStart).Select(MlSampleMapper.ToHs).ToList();
        var test = rows.Where(w => w.SemaineDebut >= testStart).Select(MlSampleMapper.ToHs).ToList();
        var testPositifs = test.Count(s => s.HsPositive);

        if (train.Count == 0 || test.Count == 0 || testPositifs < _options.MinTestPositives)
        {
            var reason = $"HS : jeu de test insuffisant ({test.Count} semaines, {testPositifs} positives).";
            _logger.LogInformation("{Reason}", reason);
            await PersistRejectionAsync(bddSage, MlModelType.HsProbabilite, reason, rows.Count, null, from, to, ct);
            await PersistRejectionAsync(bddSage, MlModelType.HsMontant, reason, rows.Count, null, from, to, ct);
            return new HsOutcome(null, null, null, reason);
        }

        var baselineAuc = Auc(
            test.Select(s => (double)s.HsMoyenne4Semaines).ToList(),
            test.Select(s => s.HsPositive).ToList());
        var baselineMae = test.Average(s => Math.Abs((double)s.HsAmount - s.HsMoyenne4Semaines));

        var probability = _trainer.TrainHsProbability(train, test, _options.OvertimeAlgorithm, ct);
        var amount = _trainer.TrainHsAmount(train, test, _options.OvertimeAlgorithm, ct);

        var probAuc = probability.Metrics.Auc ?? 0d;
        var probAccepted = probability.Accepted
                           && probability.Model is { Length: > 0 }
                           && probAuc >= _options.AbsenceMinAuc
                           && probAuc >= baselineAuc + _options.AbsenceAucImprovement;

        var amountMae = amount.Metrics.Mae ?? double.MaxValue;
        var amountAccepted = amount.Accepted
                             && amount.Model is { Length: > 0 }
                             && amountMae <= baselineMae * (1 - _options.OvertimeMaeImprovement);

        var probMetrics = probability.Metrics with { BaselineAuc = baselineAuc };
        var amountMetrics = amount.Metrics with { BaselineMae = baselineMae };

        var probModel = await PersistAsync(
            bddSage, MlModelType.HsProbabilite, probability, probMetrics, from, to,
            rows.Count, rows.Count(w => w.HS > 0), probAccepted,
            probAccepted ? null : $"P(HS>0) : AUC {probAuc:F3}, baseline {baselineAuc:F3}.", ct);

        var amountModel = await PersistAsync(
            bddSage, MlModelType.HsMontant, amount, amountMetrics, from, to,
            rows.Count, rows.Count(w => w.HS > 0), amountAccepted,
            amountAccepted ? null : $"Montant HS : MAE {amountMae:F2}, baseline {baselineMae:F2}.", ct);

        _logger.LogInformation(
            "HS {Sage} : AUC={Auc:F3} (baseline {BaselineAuc:F3}), MAE={Mae:F2} (baseline {BaselineMae:F2}).",
            bddSage, probAuc, baselineAuc, amountMae, baselineMae);

        var message = probAccepted && amountAccepted
            ? $"HS : modèles {probModel.Version} actifs (AUC {probAuc:F3}, MAE {amountMae:F2} vs {baselineMae:F2})."
            : $"HS : {(probAccepted ? "probabilité active" : "probabilité en baseline")}, " +
              $"{(amountAccepted ? "montant actif" : "montant en baseline")}.";

        return new HsOutcome(probModel, amountModel, amountMetrics, message);
    }

    // ------------------------------------------------------------------ persistance

    /// <summary>
    /// Trace un entraînement non réalisé (données insuffisantes) pour que l'UI explique le mode
    /// baseline au lieu d'afficher un simple « aucun modèle ». Les lignes rejetées sont purgées
    /// comme les anciens modèles.
    /// </summary>
    private async Task PersistRejectionAsync(
        string bddSage,
        MlModelType type,
        string reason,
        int rows,
        int? positifs,
        DateTime from,
        DateTime to,
        CancellationToken ct)
    {
        var model = new T_ML_MODELE
        {
            BddSage = bddSage,
            TypeModele = MlModelTypes.ToStorage(type),
            Version = $"{DateTime.Now:yyyyMMdd-HHmm}-baseline",
            Algorithme = "aucun",
            Statut = MlModelStatus.Rejete,
            EstActif = false,
            EntraineLe = DateTime.Now,
            PeriodeDebut = from,
            PeriodeFin = to,
            NbLignes = rows,
            NbPositifs = positifs,
            MetriquesJson = "{}",
            SchemaHash = _trainer.FeatureSchemaHash,
            ModeleBinaire = null,
            Commentaire = reason
        };

        await _store.SaveModelAsync(model, ct);
        await _store.PurgeModelsAsync(bddSage, type, _options.ModelRetentionPerType, ct);
    }

    private async Task<T_ML_MODELE> PersistAsync(
        string bddSage,
        MlModelType type,
        MlTrainingResult result,
        MlMetrics metrics,
        DateTime from,
        DateTime to,
        int rows,
        int? positifs,
        bool accepted,
        string? commentaire,
        CancellationToken ct)
    {
        if (accepted)
            await _store.ArchiveActiveModelsAsync(bddSage, type, ct);

        var model = new T_ML_MODELE
        {
            BddSage = bddSage,
            TypeModele = MlModelTypes.ToStorage(type),
            Version = $"{DateTime.Now:yyyyMMdd-HHmm}-{result.Algorithm}",
            Algorithme = result.Algorithm,
            Statut = accepted ? MlModelStatus.Active : MlModelStatus.Rejete,
            EstActif = accepted,
            EntraineLe = DateTime.Now,
            PeriodeDebut = from,
            PeriodeFin = to,
            NbLignes = rows,
            NbPositifs = positifs,
            MetriquesJson = JsonSerializer.Serialize(metrics),
            SchemaHash = _trainer.FeatureSchemaHash,
            ModeleBinaire = accepted ? result.Model : null,
            Commentaire = commentaire
        };

        await _store.SaveModelAsync(model, ct);

        if (accepted)
            await _store.PurgeModelsAsync(bddSage, type, _options.ModelRetentionPerType, ct);

        return model;
    }

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

    private sealed record AbsenceOutcome(T_ML_MODELE? Absence, MlMetrics? AbsenceMetrics, string Message);
    private sealed record HsOutcome(
        T_ML_MODELE? HsProbabilite, T_ML_MODELE? HsMontant, MlMetrics? HsMetrics, string Message);
}
