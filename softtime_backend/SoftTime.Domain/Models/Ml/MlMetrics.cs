namespace SoftTime.Domain.Models.Ml;

/// <summary>Métriques d'évaluation d'un modèle, comparées à la baseline statistique.</summary>
public sealed record MlMetrics(
    double? Auc = null,
    double? PrAuc = null,
    double? Brier = null,
    double? Mae = null,
    double? Rmse = null,
    double? R2 = null,
    double? BaselineAuc = null,
    double? BaselineMae = null,
    // 80e centile de l'erreur absolue du montant HS sur le jeu de test : demi-largeur de l'intervalle affiché.
    double? ErreurP80 = null,
    int TestRows = 0,
    int TestPositives = 0);
