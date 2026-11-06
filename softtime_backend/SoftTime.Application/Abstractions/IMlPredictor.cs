using SoftTime.Domain.Models.Ml;

namespace SoftTime.Application.Abstractions;

/// <summary>Inférence en lot à partir d'un binaire de modèle ML.NET.</summary>
public interface IMlPredictor
{
    /// <summary>Contrat de features attendu : un modèle stocké avec un autre hash est inutilisable.</summary>
    string FeatureSchemaHash { get; }

    IReadOnlyList<float> PredictAbsence(byte[] model, IReadOnlyList<MlAbsenceSample> samples);
    IReadOnlyList<float> PredictHsProbability(byte[] model, IReadOnlyList<MlHsSample> samples);
    IReadOnlyList<float> PredictHsAmount(byte[] model, IReadOnlyList<MlHsSample> samples);
}
