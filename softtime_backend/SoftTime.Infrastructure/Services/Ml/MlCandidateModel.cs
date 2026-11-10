using SoftTime.Domain.Models.Ml;

namespace SoftTime.Infrastructure.Services.Ml;

/// <summary>Modèle entraîné, métriques enrichies de la baseline et décision d'activation.</summary>
internal sealed record MlCandidateModel(MlTrainingResult Result, MlMetrics Metrics, bool Accepted, string? Commentaire);
