namespace SoftTime.Domain.Models.Ml;

/// <summary>Types de modèles entraînés, stockés tels quels dans <c>ml.Modele.TypeModele</c>.</summary>
public enum MlModelType
{
    Absence,
    HsProbabilite,
    HsMontant
}

public static class MlModelTypes
{
    public const string Absence = "Absence";
    public const string HsProbabilite = "HsProbabilite";
    public const string HsMontant = "HsMontant";

    public static string ToStorage(MlModelType type) => type switch
    {
        MlModelType.Absence => Absence,
        MlModelType.HsProbabilite => HsProbabilite,
        MlModelType.HsMontant => HsMontant,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public static MlModelType Parse(string value) => value switch
    {
        Absence => MlModelType.Absence,
        HsProbabilite => MlModelType.HsProbabilite,
        HsMontant => MlModelType.HsMontant,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Type de modèle inconnu.")
    };
}

/// <summary>Version du contrat de features, stockée dans les instantanés et le registre.</summary>
public static class MlSchema
{
    public const string Version = "v1";
}

/// <summary>Échantillon d'entraînement/prédiction pour l'absentéisme (grain salarié × jour).</summary>
public sealed class MlAbsenceSample
{
    // Label et pondération (déséquilibre des classes).
    public bool Label { get; set; }
    public float Weight { get; set; } = 1f;

    // Calendrier.
    public float JourSemaine { get; set; }
    public float SemaineIso { get; set; }
    public float Mois { get; set; }
    public float JourDuMois { get; set; }
    public bool VeilleFerie { get; set; }
    public bool LendemainFerie { get; set; }
    public bool DebutMois { get; set; }
    public bool FinMois { get; set; }

    // Salarié.
    public float CategorieId { get; set; }
    public string Departement { get; set; } = string.Empty;
    public float HeuresSemaineContrat { get; set; }
    public bool MultiPointage { get; set; }
    public float AncienneteJours { get; set; }

    // Historique as-of J-1.
    public float Absences7j { get; set; }
    public float Absences30j { get; set; }
    public float Absences90j { get; set; }
    public float TauxAbsence30j { get; set; }
    public float Retards30j { get; set; }
    public float JoursTravailles7j { get; set; }
    public float JoursDepuisDerniereAbsence { get; set; }
    public float HeuresSupp4Semaines { get; set; }

    // Collectif.
    public float TauxAbsenceEquipe30j { get; set; }
    public float EffectifPrevuJour { get; set; }
    public float CongesPrevusJour { get; set; }

    // Jour demandé (connu à l'avance).
    public float HeuresPlanifiees { get; set; }
    public bool PlanningConnu { get; set; }
}

/// <summary>Échantillon d'entraînement/prédiction pour les heures supplémentaires (grain salarié × semaine).</summary>
public sealed class MlHsSample
{
    public bool HsPositive { get; set; }
    public float HsAmount { get; set; }

    public float SemaineIso { get; set; }
    public float Mois { get; set; }
    public float HeuresPlanifiees { get; set; }
    public float HeuresTravaillees { get; set; }
    public float NbJoursPlanifies { get; set; }
    public float NbJoursTravailles { get; set; }
    public float CongeJours { get; set; }

    public float HsMoyenne4Semaines { get; set; }
    public float HsMoyenne8Semaines { get; set; }
    public float Absences4Semaines { get; set; }
    public float Retards4Semaines { get; set; }
    public float HeuresTravaillees4Semaines { get; set; }

    public float CategorieId { get; set; }
    public string Departement { get; set; } = string.Empty;
    public float HeuresSemaineContrat { get; set; }
    public bool MultiPointage { get; set; }
}

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
    int TestRows = 0,
    int TestPositives = 0);

/// <summary>Résultat d'un entraînement : modèle accepté ou rejeté avec sa raison.</summary>
public sealed record MlTrainingResult(
    bool Accepted,
    string Algorithm,
    byte[]? Model,
    MlMetrics Metrics,
    string? RejectReason);
