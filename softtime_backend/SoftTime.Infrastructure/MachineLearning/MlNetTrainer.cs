using Microsoft.ML;
using Microsoft.ML.Trainers;
using Microsoft.ML.Trainers.FastTree;
using SoftTime.Application.Abstractions;
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

        var ml = new MLContext(seed: 1);
        var trainData = ml.Data.LoadFromEnumerable(train.Select(MlNetData.Map));
        var testData = ml.Data.LoadFromEnumerable(test.Select(MlNetData.Map));

        var prep = ml.Transforms.Categorical.OneHotEncoding("DepartementEncoded", nameof(MlAbsenceData.Departement))
            .Append(ml.Transforms.Concatenate("Features", MlNetData.AbsenceFeatures));

        ITransformer model;
        var sdca = UseSdca(algorithm);
        if (sdca)
        {
            model = prep.Append(ml.BinaryClassification.Trainers.SdcaLogisticRegression(
                new SdcaLogisticRegressionBinaryTrainer.Options
                {
                    LabelColumnName = nameof(MlAbsenceData.Label),
                    FeatureColumnName = "Features",
                    MaximumNumberOfIterations = 100
                })).Fit(trainData);
        }
        else
        {
            model = prep.Append(ml.BinaryClassification.Trainers.FastTree(
                new FastTreeBinaryTrainer.Options
                {
                    LabelColumnName = nameof(MlAbsenceData.Label),
                    FeatureColumnName = "Features",
                    NumberOfLeaves = 16,
                    NumberOfTrees = 80,
                    MinimumExampleCountPerLeaf = 10
                })).Fit(trainData);
        }

        var predictions = model.Transform(testData);
        var metrics = ml.BinaryClassification.Evaluate(
            predictions, labelColumnName: nameof(MlAbsenceData.Label));

        var rows = ml.Data
            .CreateEnumerable<MlAbsencePrediction>(predictions, reuseRowObject: false)
            .ToList();
        var brier = rows.Count == 0
            ? (double?)null
            : rows.Average(r => Math.Pow(r.Probability - (r.Label ? 1 : 0), 2));

        return new MlTrainingResult(
            Accepted: true,
            Algorithm: sdca ? "SdcaLogisticRegression" : "FastTree",
            Model: Save(ml, model, trainData),
            Metrics: new MlMetrics(
                Auc: metrics.AreaUnderRocCurve,
                PrAuc: metrics.AreaUnderPrecisionRecallCurve,
                Brier: brier,
                TestRows: rows.Count,
                TestPositives: test.Count(x => x.Label)),
            RejectReason: null);
    }

    public MlTrainingResult TrainHsProbability(
        IReadOnlyList<MlHsSample> train,
        IReadOnlyList<MlHsSample> test,
        string algorithm,
        CancellationToken ct = default)
    {
        if (train.Count == 0 || test.Count == 0)
            return Rejected("Jeu d'entraînement ou de test vide.");

        var ml = new MLContext(seed: 1);
        var trainData = ml.Data.LoadFromEnumerable(train.Select(MlNetData.Map));
        var testData = ml.Data.LoadFromEnumerable(test.Select(MlNetData.Map));

        var prep = ml.Transforms.Categorical.OneHotEncoding("DepartementEncoded", nameof(MlHsData.Departement))
            .Append(ml.Transforms.Concatenate("Features", MlNetData.HsFeatures));

        ITransformer model;
        var sdca = UseSdca(algorithm);
        if (sdca)
        {
            model = prep.Append(ml.BinaryClassification.Trainers.SdcaLogisticRegression(
                new SdcaLogisticRegressionBinaryTrainer.Options
                {
                    LabelColumnName = nameof(MlHsData.HsPositive),
                    FeatureColumnName = "Features",
                    MaximumNumberOfIterations = 100
                })).Fit(trainData);
        }
        else
        {
            model = prep.Append(ml.BinaryClassification.Trainers.FastTree(
                new FastTreeBinaryTrainer.Options
                {
                    LabelColumnName = nameof(MlHsData.HsPositive),
                    FeatureColumnName = "Features",
                    NumberOfLeaves = 12,
                    NumberOfTrees = 60,
                    MinimumExampleCountPerLeaf = 10
                })).Fit(trainData);
        }

        var predictions = model.Transform(testData);
        var metrics = ml.BinaryClassification.Evaluate(
            predictions, labelColumnName: nameof(MlHsData.HsPositive));

        var rows = ml.Data
            .CreateEnumerable<MlHsProbabilityPrediction>(predictions, reuseRowObject: false)
            .ToList();
        var brier = rows.Count == 0
            ? (double?)null
            : rows.Average(r => Math.Pow(r.Probability - (r.HsPositive ? 1 : 0), 2));

        return new MlTrainingResult(
            Accepted: true,
            Algorithm: sdca ? "SdcaLogisticRegression" : "FastTree",
            Model: Save(ml, model, trainData),
            Metrics: new MlMetrics(
                Auc: metrics.AreaUnderRocCurve,
                PrAuc: metrics.AreaUnderPrecisionRecallCurve,
                Brier: brier,
                TestRows: rows.Count,
                TestPositives: test.Count(x => x.HsPositive)),
            RejectReason: null);
    }

    public MlTrainingResult TrainHsAmount(
        IReadOnlyList<MlHsSample> train,
        IReadOnlyList<MlHsSample> test,
        string algorithm,
        CancellationToken ct = default)
    {
        if (train.Count == 0 || test.Count == 0)
            return Rejected("Jeu d'entraînement ou de test vide.");

        var ml = new MLContext(seed: 1);
        var trainData = ml.Data.LoadFromEnumerable(train.Select(MlNetData.Map));
        var testData = ml.Data.LoadFromEnumerable(test.Select(MlNetData.Map));

        var prep = ml.Transforms.Categorical.OneHotEncoding("DepartementEncoded", nameof(MlHsData.Departement))
            .Append(ml.Transforms.Concatenate("Features", MlNetData.HsFeatures));

        ITransformer model;
        var sdca = UseSdca(algorithm);
        if (sdca)
        {
            model = prep.Append(ml.Regression.Trainers.Sdca(
                new SdcaRegressionTrainer.Options
                {
                    LabelColumnName = nameof(MlHsData.HsAmount),
                    FeatureColumnName = "Features",
                    MaximumNumberOfIterations = 100
                })).Fit(trainData);
        }
        else
        {
            model = prep.Append(ml.Regression.Trainers.FastTree(
                new FastTreeRegressionTrainer.Options
                {
                    LabelColumnName = nameof(MlHsData.HsAmount),
                    FeatureColumnName = "Features",
                    NumberOfLeaves = 12,
                    NumberOfTrees = 60,
                    MinimumExampleCountPerLeaf = 10
                })).Fit(trainData);
        }

        var predictions = model.Transform(testData);
        var metrics = ml.Regression.Evaluate(
            predictions, labelColumnName: nameof(MlHsData.HsAmount));

        return new MlTrainingResult(
            Accepted: true,
            Algorithm: sdca ? "SdcaRegression" : "FastTree",
            Model: Save(ml, model, trainData),
            Metrics: new MlMetrics(
                Mae: metrics.MeanAbsoluteError,
                Rmse: metrics.RootMeanSquaredError,
                R2: metrics.RSquared,
                TestRows: test.Count),
            RejectReason: null);
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
