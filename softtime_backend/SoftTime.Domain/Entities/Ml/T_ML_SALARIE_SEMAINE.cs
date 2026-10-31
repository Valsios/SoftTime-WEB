using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.Ml;

/// <summary>
/// Instantané analytique au grain salarié × semaine ISO (lundi → dimanche). Le label
/// <see cref="HS"/> reprend la valeur validée de <c>T_HS_VALIDER</c> quand elle existe, sinon le
/// résultat de <c>OvertimeCalculator</c> — c'est-à-dire la définition métier des heures supplémentaires.
/// </summary>
[Table("SalarieSemaine", Schema = "ml")]
public class T_ML_SALARIE_SEMAINE
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public string BddSage { get; set; } = string.Empty;
    public string Matricule { get; set; } = string.Empty;

    /// <summary>Lundi de la semaine ISO.</summary>
    public DateTime SemaineDebut { get; set; }

    public byte SemaineIso { get; set; }
    public short AnneeIso { get; set; }

    public decimal HeuresPlanifiees { get; set; }
    public decimal HeuresTravaillees { get; set; }
    public int NbJoursPlanifies { get; set; }
    public int NbJoursTravailles { get; set; }
    public decimal CongeJours { get; set; }

    // ---------------------------------------------------------------- labels
    public decimal HS { get; set; }
    public bool HSValidee { get; set; }
    public decimal Nuit { get; set; }
    public decimal Dimanche { get; set; }
    public decimal Ferie { get; set; }
    public int RetardMinutes { get; set; }
    public decimal AbsenceHeures { get; set; }

    // ---------------------------------------------------------------- features (as-of S-1)
    public decimal HsMoyenne4Semaines { get; set; }
    public decimal HsMoyenne8Semaines { get; set; }
    public int Absences4Semaines { get; set; }
    public int Retards4Semaines { get; set; }
    public decimal HeuresTravaillees4Semaines { get; set; }
    public int? CategorieId { get; set; }
    public string? Departement { get; set; }
    public decimal? HeuresSemaineContrat { get; set; }
    public bool MultiPointage { get; set; }

    public DateTime SourceGenereeLe { get; set; }
    public string VersionSchema { get; set; } = string.Empty;
}
