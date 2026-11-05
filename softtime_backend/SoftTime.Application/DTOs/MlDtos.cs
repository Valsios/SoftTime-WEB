using SoftTime.Domain.Models.Ml;

namespace SoftTime.Application.DTOs;

/// <summary>Modèle exposé au frontend (sans le binaire ML.NET).</summary>
public sealed record MlModelDto(
    int Id,
    string TypeModele,
    string Version,
    string Algorithme,
    string Statut,
    bool EstActif,
    DateTime EntraineLe,
    DateTime PeriodeDebut,
    DateTime PeriodeFin,
    int NbLignes,
    int? NbPositifs,
    string MetriquesJson,
    string? Commentaire);

/// <summary>État du moteur pour la société active.</summary>
public sealed record MlStatusDto(
    bool Enabled,
    string Etat,
    string? Message,
    DateTime? DerniereGeneration,
    // Plafond hebdomadaire des HS exonérées (au-delà, imposables) : seuil de pilotage.
    decimal PlafondHsExonere,
    IReadOnlyList<MlModelDto> Modeles);

public sealed record MlHsForecastDto(
    string Matricule,
    string? Nom,
    string? Prenom,
    string? Departement,
    DateTime SemaineDebut,
    decimal HeuresPrevues,
    decimal BorneBasse,
    decimal BorneHaute,
    double ProbabiliteDepassement,
    byte Niveau);

/// <summary>Totaux HS prévus d'une semaine de l'horizon.</summary>
public sealed record MlHsWeekDto(
    DateTime SemaineDebut,
    int Salaries,
    // Salariés dont les HS prévues dépassent le plafond exonéré.
    int AuDelaPlafond,
    decimal HeuresPrevues,
    decimal BorneBasse,
    decimal BorneHaute);

/// <summary>Prévu / réel d'une semaine terminée (salariés présents des deux côtés).</summary>
public sealed record MlHsAccuracyDto(
    DateTime SemaineDebut,
    int Salaries,
    decimal HeuresPrevues,
    decimal HeuresReelles,
    decimal EcartMoyen,
    double Couverture);

public sealed record MlMetricsDto(
    double? Auc,
    double? PrAuc,
    double? Brier,
    double? Mae,
    double? Rmse,
    double? R2,
    double? BaselineAuc,
    double? BaselineMae,
    int TestRows,
    int TestPositives)
{
    public static MlMetricsDto? From(MlMetrics? metrics) => metrics is null
        ? null
        : new MlMetricsDto(
            metrics.Auc, metrics.PrAuc, metrics.Brier, metrics.Mae, metrics.Rmse, metrics.R2,
            metrics.BaselineAuc, metrics.BaselineMae, metrics.TestRows, metrics.TestPositives);
}

public sealed record MlTrainingOutcomeDto(
    string Etat,
    string Message,
    string? HsProbabiliteVersion,
    string? HsMontantVersion,
    MlMetricsDto? HsMetriques);

public sealed record MlPipelineResultDto(
    string Etat,
    string Message,
    int DailyRows,
    int WeeklyRows,
    int HsPredictions);
