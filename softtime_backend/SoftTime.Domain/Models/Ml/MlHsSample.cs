namespace SoftTime.Domain.Models.Ml;

/// <summary>Échantillon d'entraînement/prédiction pour les heures supplémentaires (grain salarié × semaine).</summary>
public sealed class MlHsSample
{
    public bool HsPositive { get; set; }
    public float HsAmount { get; set; }

    // Uniquement des valeurs connues avant la semaine cible : les heures travaillées de la semaine
    // elle-même donneraient la réponse à l'entraînement et vaudraient 0 en prévision.
    public float SemaineIso { get; set; }
    public float Mois { get; set; }
    public float HeuresPlanifiees { get; set; }
    public float NbJoursPlanifies { get; set; }
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
