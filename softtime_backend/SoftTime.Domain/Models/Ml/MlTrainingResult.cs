namespace SoftTime.Domain.Models.Ml;

/// <summary>Résultat d'un entraînement : modèle accepté ou rejeté avec sa raison.</summary>
public sealed record MlTrainingResult(
    bool Accepted,
    string Algorithm,
    byte[]? Model,
    MlMetrics Metrics,
    string? RejectReason);
