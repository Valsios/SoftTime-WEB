using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Services;

/// <summary>
/// Décisions du pré-traitement des pointages, calculées avant toute écriture. Permet la
/// simulation (dry-run) et rend le traitement vérifiable sans base de données.
/// </summary>
internal sealed class PreprocessPlan
{
    /// <summary>Heures corrigées à créer.</summary>
    public List<PreprocessCorrectionPlan> Corrections { get; } = new();

    /// <summary>Anomalies à créer.</summary>
    public List<PreprocessAnomalyPlan> Anomalies { get; } = new();

    /// <summary>Lignes <c>AUTO</c> de la période à purger avant régénération.</summary>
    public List<T_HEURECORRIGER> CorrectionsAPurger { get; } = new();

    /// <summary>Anomalies devenues sans objet (jour redevenu cohérent, ou jour de congé).</summary>
    public List<T_HEUREANOMALIE> AnomaliesASupprimer { get; } = new();

    /// <summary>Jours protégés (correction manuelle ou semaine validée) laissés intacts.</summary>
    public int JoursIgnores { get; set; }

    /// <summary>Pointages écartés par le filtre « base pointeuse active ».</summary>
    public int PointagesEcartes { get; set; }
}

internal sealed record PreprocessCorrectionPlan(
    string Matricule,
    DateTime DateIn,
    DateTime DateOut,
    TimeSpan HEntree,
    TimeSpan HSortie,
    TimeSpan? Hps,
    TimeSpan? Hpe,
    bool EstFerie);

internal sealed record PreprocessAnomalyPlan(
    string Matricule,
    DateTime Jour,
    string Motif,
    TimeSpan? H1,
    TimeSpan? H2,
    bool? AbsenceAm,
    bool? AbsencePm,
    bool EstFerie);
