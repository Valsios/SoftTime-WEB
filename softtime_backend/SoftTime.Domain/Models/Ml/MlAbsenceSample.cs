namespace SoftTime.Domain.Models.Ml;

/// <summary>Échantillon d'entraînement/prédiction pour l'absentéisme (grain salarié × jour).</summary>
public sealed class MlAbsenceSample
{
    public bool Label { get; set; }

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
