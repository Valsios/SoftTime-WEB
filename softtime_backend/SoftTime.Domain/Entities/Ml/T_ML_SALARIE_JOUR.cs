using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.Ml;

/// <summary>
/// Instantané analytique au grain salarié × jour. Les colonnes de features sont calculées
/// uniquement avec les données connues la veille (as-of J-1) ; les colonnes de label figent ce
/// qui s'est réellement produit le jour J. C'est cette table — et non les tables métier — qui
/// sert à entraîner le modèle d'absentéisme.
/// </summary>
[Table("SalarieJour", Schema = "ml")]
public class T_ML_SALARIE_JOUR
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public string BddSage { get; set; } = string.Empty;

    /// <summary>Clé canonique du matricule (<c>MatriculeIdentity.Cle</c>).</summary>
    public string Matricule { get; set; } = string.Empty;

    [Column("Date")]
    public DateTime Date { get; set; }

    // ---------------------------------------------------------------- population / statut
    public bool EstJourPlanifie { get; set; }
    public bool EstOff { get; set; }
    public bool EstFerie { get; set; }
    public bool EstWeekend { get; set; }
    public bool EstConge { get; set; }
    public bool PlanningConnu { get; set; }
    public decimal? HeuresPlanifiees { get; set; }
    public decimal? HeuresTravaillees { get; set; }
    public int? RetardMinutes { get; set; }

    // ---------------------------------------------------------------- labels
    /// <summary>Absence non justifiée : jour planifié avec heures et sans aucun pointage.</summary>
    public bool AbsenceNonJustifiee { get; set; }

    /// <summary>Absence déclarée (ABSENCE_AM / ABSENCE_PM) : elle n'est pas la cible du modèle.</summary>
    public bool AbsenceDeclaree { get; set; }

    public decimal? AbsenceHeures { get; set; }

    // ---------------------------------------------------------------- features calendrier
    public byte JourSemaine { get; set; }
    public byte SemaineIso { get; set; }
    public short AnneeIso { get; set; }
    public byte Mois { get; set; }
    public byte JourDuMois { get; set; }
    public bool VeilleFerie { get; set; }
    public bool LendemainFerie { get; set; }
    public bool DebutMois { get; set; }
    public bool FinMois { get; set; }

    // ---------------------------------------------------------------- features salarié
    public int? CategorieId { get; set; }
    public string? Departement { get; set; }
    public decimal? HeuresSemaineContrat { get; set; }
    public bool MultiPointage { get; set; }
    public int? AncienneteJours { get; set; }

    // ---------------------------------------------------------------- features historiques (as-of J-1)
    public int Absences7j { get; set; }
    public int Absences30j { get; set; }
    public int Absences90j { get; set; }
    public double TauxAbsence30j { get; set; }
    public int Retards30j { get; set; }
    public int JoursTravailles7j { get; set; }
    public int? JoursDepuisDerniereAbsence { get; set; }
    public decimal HeuresSupp4Semaines { get; set; }

    // ---------------------------------------------------------------- features collectives
    public double TauxAbsenceEquipe30j { get; set; }
    public int EffectifPrevuJour { get; set; }
    public int CongesPrevusJour { get; set; }

    // ---------------------------------------------------------------- traçabilité
    public DateTime SourceGenereeLe { get; set; }
    public string VersionSchema { get; set; } = string.Empty;
}
