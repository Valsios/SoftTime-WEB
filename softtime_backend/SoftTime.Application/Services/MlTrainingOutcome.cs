using SoftTime.Domain.Entities.Ml;
using SoftTime.Domain.Models.Ml;

namespace SoftTime.Application.Services;

/// <summary>Résultat d'un cycle d'entraînement pour la société active.</summary>
public sealed record MlTrainingOutcome(
    string Etat,
    string Message,
    T_ML_MODELE? HsProbabilite,
    T_ML_MODELE? HsMontant,
    MlMetrics? HsMetrics);
