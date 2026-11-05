namespace SoftTime.Application.Services;

/// <summary>Paramétrage du moteur prédictif (section <c>Ml</c> de appsettings.json).</summary>
public sealed class MlOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Profondeur d'historique reconstruite par le backfill initial.</summary>
    public int HistoryMonths { get; set; } = 18;

    /// <summary>Fenêtre utilisée pour l'entraînement (plus courte que l'historique stocké).</summary>
    public int TrainingMonths { get; set; } = 6;

    /// <summary>Plafond de lignes chargées en mémoire pour l'entraînement (les plus récentes).</summary>
    public int MaxTrainingRows { get; set; } = 200_000;

    /// <summary>Plafond de semaines chargées en mémoire pour l'entraînement HS.</summary>
    public int MaxTrainingWeeks { get; set; } = 25_000;

    /// <summary>Taille des lots de reconstruction (bornes mémoire).</summary>
    public int QuarterChunkMonths { get; set; } = 3;

    /// <summary>Fenêtre de chauffe pour les features glissantes (7/30/90 jours, 4/8 semaines).</summary>
    public int WarmupDays { get; set; } = 120;

    /// <summary>Fenêtre de correction glissante : les semaines récentes sont recalculées.</summary>
    public int RefreshWindowWeeks { get; set; } = 12;

    public int MinDailyRows { get; set; } = 500;
    public int MinAbsencePositives { get; set; } = 30;
    public int MinWeeklyRows { get; set; } = 200;
    public int MinTestPositives { get; set; } = 10;

    /// <summary>Nombre de semaines du jeu de test chronologique.</summary>
    public int TestWindowWeeks { get; set; } = 8;

    public int RetrainIntervalDays { get; set; } = 7;

    /// <summary>Heure locale du rafraîchissement quotidien.</summary>
    public int RefreshHourLocal { get; set; } = 2;

    public int ForecastHorizonDays { get; set; } = 14;
    public int ForecastHorizonWeeks { get; set; } = 4;

    public string AbsenceAlgorithm { get; set; } = "FastTree";
    public string OvertimeAlgorithm { get; set; } = "FastTree";

    public int ModelRetentionPerType { get; set; } = 5;

    /// <summary>Seuils du risque d'absence d'un jour (probabilité journalière).</summary>
    public double LowRiskThreshold { get; set; } = 0.15;
    public double HighRiskThreshold { get; set; } = 0.35;

    /// <summary>
    /// Seuils du risque sur l'horizon, relatifs : absences attendues du salarié rapportées à celles
    /// d'un salarié moyen sur les mêmes jours (1,5 = 50 % de plus que la moyenne).
    /// </summary>
    public double HorizonMediumRiskRatio { get; set; } = 1.5;
    public double HorizonHighRiskRatio { get; set; } = 2.5;

    /// <summary>
    /// Qualité des labels : un jour où au moins cette part des salariés planifiés n'a aucun pointage
    /// est traité comme un trou de données (import ou traitement non fait), pas comme des absences.
    /// </summary>
    public double MissingPunchDayAbsenceRate { get; set; } = 0.5;

    /// <summary>Nombre minimal de salariés planifiés pour juger un jour « sans pointage ».</summary>
    public int MissingPunchDayMinPlanned { get; set; } = 5;

    /// <summary>Sans aucun pointage sur cette période récente, les prévisions sont suspendues.</summary>
    public int RecentDataDays { get; set; } = 14;

    /// <summary>Porte qualité : AUC minimale du modèle d'absence.</summary>
    public double AbsenceMinAuc { get; set; } = 0.55;

    /// <summary>Porte qualité : gain d'AUC par rapport à la baseline.</summary>
    public double AbsenceAucImprovement { get; set; } = 0.02;

    /// <summary>Porte qualité : réduction minimale de MAE par rapport à la baseline HS.</summary>
    public double OvertimeMaeImprovement { get; set; } = 0.05;
}
