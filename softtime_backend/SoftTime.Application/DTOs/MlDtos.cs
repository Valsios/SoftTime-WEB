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
    IReadOnlyList<MlModelDto> Modeles);

public sealed record MlAbsenceDayDto(
    DateTime Date,
    double Probabilite,
    byte Niveau,
    bool PlanningConnu);

public sealed record MlAbsenceForecastDto(
    string Matricule,
    string? Nom,
    string? Prenom,
    string? Departement,
    double Risque,
    byte Niveau,
    IReadOnlyList<MlAbsenceDayDto> Jours);

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
    string? AbsenceVersion,
    string? HsProbabiliteVersion,
    string? HsMontantVersion,
    MlMetricsDto? AbsenceMetriques,
    MlMetricsDto? HsMetriques);

public sealed record MlPipelineResultDto(
    string Etat,
    string Message,
    int DailyRows,
    int WeeklyRows,
    int AbsencePredictions,
    int HsPredictions);
