using SoftTime.Domain.Models.Ml;

namespace SoftTime.Application.Abstractions.Ml;

/// <summary>Entraînement des modèles ML.NET. Les métriques sont calculées sur un jeu de test chronologique.</summary>
public interface IMlTrainer
{
    /// <summary>SHA-256 du contrat de features : stocké dans le registre pour invalider un modèle incompatible.</summary>
    string FeatureSchemaHash { get; }

    MlTrainingResult TrainAbsence(
        IReadOnlyList<MlAbsenceSample> train,
        IReadOnlyList<MlAbsenceSample> test,
        string algorithm,
        CancellationToken ct = default);

    MlTrainingResult TrainHsProbability(
        IReadOnlyList<MlHsSample> train,
        IReadOnlyList<MlHsSample> test,
        string algorithm,
        CancellationToken ct = default);

    MlTrainingResult TrainHsAmount(
        IReadOnlyList<MlHsSample> train,
        IReadOnlyList<MlHsSample> test,
        string algorithm,
        CancellationToken ct = default);
}
