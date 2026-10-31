using Microsoft.ML;
using SoftTime.Application.Abstractions;
using SoftTime.Domain.Models.Ml;

namespace SoftTime.Infrastructure.MachineLearning;

/// <summary>
/// Inférence en lot à partir des binaires stockés dans <c>ml.Modele</c>. <c>Transform</c> est
/// thread-safe et ne nécessite pas de <c>PredictionEnginePool</c> puisque les lectures HTTP
/// consomment des prédictions déjà calculées.
/// </summary>
public sealed class MlNetPredictor : IMlPredictor
{
    public IReadOnlyList<float> PredictAbsence(byte[] model, IReadOnlyList<MlAbsenceSample> samples)
    {
        if (samples.Count == 0)
            return Array.Empty<float>();

        var ml = new MLContext();
        var transformer = Load(ml, model);
        var data = ml.Data.LoadFromEnumerable(samples.Select(MlNetData.Map));
        var predictions = transformer.Transform(data);
        return ml.Data
            .CreateEnumerable<MlAbsencePrediction>(predictions, reuseRowObject: false)
            .Select(r => r.Probability)
            .ToList();
    }

    public IReadOnlyList<float> PredictHsProbability(byte[] model, IReadOnlyList<MlHsSample> samples)
    {
        if (samples.Count == 0)
            return Array.Empty<float>();

        var ml = new MLContext();
        var transformer = Load(ml, model);
        var data = ml.Data.LoadFromEnumerable(samples.Select(MlNetData.Map));
        var predictions = transformer.Transform(data);
        return ml.Data
            .CreateEnumerable<MlHsProbabilityPrediction>(predictions, reuseRowObject: false)
            .Select(r => r.Probability)
            .ToList();
    }

    public IReadOnlyList<float> PredictHsAmount(byte[] model, IReadOnlyList<MlHsSample> samples)
    {
        if (samples.Count == 0)
            return Array.Empty<float>();

        var ml = new MLContext();
        var transformer = Load(ml, model);
        var data = ml.Data.LoadFromEnumerable(samples.Select(MlNetData.Map));
        var predictions = transformer.Transform(data);
        return ml.Data
            .CreateEnumerable<MlHsAmountPrediction>(predictions, reuseRowObject: false)
            .Select(r => r.Score)
            .ToList();
    }

    private static ITransformer Load(MLContext ml, byte[] model)
    {
        using var stream = new MemoryStream(model, writable: false);
        return ml.Model.Load(stream, out _);
    }
}
