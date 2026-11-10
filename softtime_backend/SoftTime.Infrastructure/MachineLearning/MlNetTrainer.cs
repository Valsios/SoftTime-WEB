using Microsoft.ML;
using Microsoft.ML.Trainers;
using Microsoft.ML.Trainers.FastTree;

using SoftTime.Application.Abstractions.Ml;
using SoftTime.Domain.Models.Ml;

namespace SoftTime.Infrastructure.MachineLearning;

/// <summary>
/// Entraînement ML.NET. Les arbres <c>FastTree</c> (100 % managés, aucune dépendance native)
/// sont le choix par défaut ; <c>Sdca</c> reste disponible pour un modèle linéaire interprétable.
/// Le jeu de test est toujours chronologique, fourni par l'appelant.
/// </summary>
public sealed class MlNetTrainer : IMlTrainer
{
    public string FeatureSchemaHash => MlNetData.ComputeSchemaHash();

    public MlTrainingResult TrainAbsence(
        IReadOnlyList<MlAbsenceSample> train,
        IReadOnlyList<MlAbsenceSample> test,
        string algorithm,
        CancellationToken ct = default)
    {
        if (train.Count == 0 || test.Count == 0)
            return Rejected("Jeu d'entraînement ou de test vide.");
        var spec = new MlNetBinarySpec(
            nameof(MlAbsenceData.Label), nameof(MlAbsenceData.Departement), MlNetData.AbsenceFeatures, Leaves: 16, Trees: 80);
        return TrainBinary<MlAbsencePrediction>(
            MlNetSession.Create(train.Select(MlNetData.Map), test.Select(MlNetData.Map)), algorithm, spec,
            r => (r.Probability, r.Label));
    }

    public MlTrainingResult TrainHsProbability(
        IReadOnlyList<MlHsSample> train,
        IReadOnlyList<MlHsSample> test,
        string algorithm,
        CancellationToken ct = default)
    {
        if (train.Count == 0 || test.Count == 0)
            return Rejected("Jeu d'entraînement ou de test vide.");
        var spec = new MlNetBinarySpec(
            nameof(MlHsData.HsPositive), nameof(MlHsData.Departement), MlNetData.HsFeatures, Leaves: 12, Trees: 60);
        return TrainBinary<MlHsProbabilityPrediction>(
            MlNetSession.Create(train.Select(MlNetData.Map), test.Select(MlNetData.Map)), algorithm, spec,
            r => (r.Probability, r.HsPositive));
    }

    public MlTrainingResult TrainHsAmount(
        IReadOnlyList<MlHsSample> train,
        IReadOnlyList<MlHsSample> test,
        string algorithm,
        CancellationToken ct = default)
    {
        if (train.Count == 0 || test.Count == 0)
            return Rejected("Jeu d'entraînement ou de test vide.");

        var session = MlNetSession.Create(train.Select(MlNetData.Map), test.Select(MlNetData.Map));
        var ml = session.Ml;
        var prep = Prep(ml, nameof(MlHsData.Departement), MlNetData.HsFeatures);
        var sdca = UseSdca(algorithm);
        var label = nameof(MlHsData.HsAmount);
        var trainer = RegressionTrainer(ml, sdca, label);
        var model = prep.Append(trainer).Fit(session.Train);
        var predictions = model.Transform(session.Test);
        var metrics = ml.Regression.Evaluate(predictions, labelColumnName: label);

        // Erreur absolue observée sur le jeu de test (prévision bornée à 0, comme à l'affichage).
        var erreurs = ErreursAbsolues(ml, predictions);

        return new MlTrainingResult(
            Accepted: true,
            Algorithm: sdca ? "SdcaRegression" : "FastTree",
            Model: Save(ml, model, session.Train),
            Metrics: new MlMetrics(
                Mae: metrics.MeanAbsoluteError,
                Rmse: metrics.RootMeanSquaredError,
                R2: metrics.RSquared,
                ErreurP80: Centile(erreurs, 0.8),
                TestRows: test.Count),
            RejectReason: null);
    }

    /// <summary>Régression SDCA ou FastTree sur la colonne « Features ».</summary>
    private static IEstimator<ITransformer> RegressionTrainer(MLContext ml, bool sdca, string label)
    {
        IEstimator<ITransformer> trainer = sdca
            ? ml.Regression.Trainers.Sdca(new SdcaRegressionTrainer.Options
            {
                LabelColumnName = label, FeatureColumnName = "Features", MaximumNumberOfIterations = 100
            })
            : ml.Regression.Trainers.FastTree(new FastTreeRegressionTrainer.Options
            {
                LabelColumnName = label, FeatureColumnName = "Features",
                NumberOfLeaves = 12, NumberOfTrees = 60, MinimumExampleCountPerLeaf = 10
            });
        return trainer;
    }

    /// <summary>Erreur absolue observée sur le jeu de test (prévision bornée à 0, comme à l'affichage), triée.</summary>
    private static List<float> ErreursAbsolues(MLContext ml, IDataView predictions)
    {
        var erreurs = ml.Data
            .CreateEnumerable<MlHsAmountPrediction>(predictions, reuseRowObject: false)
            .Select(r => Math.Abs(r.HsAmount - Math.Max(0f, r.Score)))
            .OrderBy(e => e)
            .ToList();
        return erreurs;
    }

    /// <summary>
    /// Classification binaire commune (absence, P(HS&gt;0)) : régression logistique SDCA ou FastTree,
    /// évaluée par AUC, AUC précision-rappel et score de Brier sur le jeu de test.
    /// </summary>
    private static MlTrainingResult TrainBinary<TPrediction>(
        MlNetSession session, string algorithm, MlNetBinarySpec spec, Func<TPrediction, (float Probability, bool Label)> issue)
        where TPrediction : class, new()
    {
        var ml = session.Ml;
        var prep = Prep(ml, spec.Departement, spec.Features);
        var sdca = UseSdca(algorithm);
        var trainer = BinaryTrainer(ml, sdca, spec);
        var model = prep.Append(trainer).Fit(session.Train);
        var predictions = model.Transform(session.Test);
        var metrics = ml.BinaryClassification.Evaluate(predictions, labelColumnName: spec.Label);

        var rows = ml.Data
            .CreateEnumerable<TPrediction>(predictions, reuseRowObject: false)
            .Select(issue)
            .ToList();
        var brier = rows.Count == 0
            ? (double?)null
            : rows.Average(r => Math.Pow(r.Probability - (r.Label ? 1 : 0), 2));

        return new MlTrainingResult(
            Accepted: true,
            Algorithm: sdca ? "SdcaLogisticRegression" : "FastTree",
            Model: Save(ml, model, session.Train),
            Metrics: new MlMetrics(
                Auc: metrics.AreaUnderRocCurve,
                PrAuc: metrics.AreaUnderPrecisionRecallCurve,
                Brier: brier,
                TestRows: rows.Count,
                TestPositives: rows.Count(r => r.Label)),
            RejectReason: null);
    }

    /// <summary>Classification binaire : régression logistique SDCA ou FastTree sur la colonne « Features ».</summary>
    private static IEstimator<ITransformer> BinaryTrainer(MLContext ml, bool sdca, MlNetBinarySpec spec) => sdca
        ? ml.BinaryClassification.Trainers.SdcaLogisticRegression(new SdcaLogisticRegressionBinaryTrainer.Options
        {
            LabelColumnName = spec.Label, FeatureColumnName = "Features", MaximumNumberOfIterations = 100
        })
        : ml.BinaryClassification.Trainers.FastTree(new FastTreeBinaryTrainer.Options
        {
            LabelColumnName = spec.Label, FeatureColumnName = "Features",
            NumberOfLeaves = spec.Leaves, NumberOfTrees = spec.Trees, MinimumExampleCountPerLeaf = 10
        });

    /// <summary>Encodage du département puis concaténation des features dans la colonne « Features ».</summary>
    private static IEstimator<ITransformer> Prep(MLContext ml, string departement, string[] features) =>
        ml.Transforms.Categorical.OneHotEncoding("DepartementEncoded", departement)
            .Append(ml.Transforms.Concatenate("Features", features));

    private static double? Centile(IReadOnlyList<float> triees, double centile)
    {
        if (triees.Count == 0)
            return null;
        var index = (int)Math.Ceiling(centile * triees.Count) - 1;
        return Math.Round(triees[Math.Clamp(index, 0, triees.Count - 1)], 2);
    }

    private static bool UseSdca(string algorithm)
        => string.Equals(algorithm, "Sdca", StringComparison.OrdinalIgnoreCase);

    private static byte[] Save(MLContext ml, ITransformer model, IDataView inputSchema)
    {
        using var stream = new MemoryStream();
        ml.Model.Save(model, inputSchema.Schema, stream);
        return stream.ToArray();
    }

    private static MlTrainingResult Rejected(string reason)
        => new(false, "aucun", null, new MlMetrics(), reason);
}
