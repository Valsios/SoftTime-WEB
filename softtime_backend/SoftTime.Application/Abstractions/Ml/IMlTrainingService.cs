using SoftTime.Application.Services;

namespace SoftTime.Application.Abstractions.Ml;

/// <summary>
/// Entraînement des deux modèles d'heures supplémentaires (probabilité et montant) à partir de
/// la base analytique. Le découpage est chronologique :
/// les dernières semaines servent de test, jamais un tirage aléatoire. Un modèle n'est activé que
/// s'il bat la baseline statistique et les seuils minimaux ; sinon l'ancien modèle reste actif et
/// l'état passe explicitement à « Baseline ».
/// </summary>
public interface IMlTrainingService
{
    Task<MlTrainingOutcome> TrainAsync(string bddSage, CancellationToken ct = default);
}
