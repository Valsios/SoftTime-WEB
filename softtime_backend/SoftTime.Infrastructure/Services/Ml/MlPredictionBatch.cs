using SoftTime.Domain.Entities.Ml;

namespace SoftTime.Infrastructure.Services.Ml;

/// <summary>Semaines à prédire, probabilités HS calculées et horodatage commun de la génération.</summary>
internal sealed record MlPredictionBatch(
    string Sage, DateTime GenereLe, List<MlSalarieSemaine> Weeks, IReadOnlyList<float> Probabilities);
