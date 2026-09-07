using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_HEURECORRIGER")]
public class T_HEURECORRIGER
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public decimal ID { get; set; }

    public string? BDD_SAGE { get; set; }

    public string? BDD_POINTEUSE { get; set; }

    public string? MATRICULE_SAGE { get; set; }

    public DateTime? DATE_POINTAGE_IN { get; set; }

    public DateTime? DATE_POINTAGE_OUT { get; set; }

    public TimeSpan? HENTREE { get; set; }

    public TimeSpan? HPS { get; set; }

    public TimeSpan? HPE { get; set; }

    public TimeSpan? HSORTIE { get; set; }

    public bool? ABSENCE_PM { get; set; }

    public bool? ABSENCE_AM { get; set; }

    public string? INTITULE_ABSENCE { get; set; }

    public TimeSpan? RETARD { get; set; }

    public bool? FERIER { get; set; }

    public bool? FERIER_AM { get; set; }

    public bool? FERIER_PM { get; set; }

    public bool? IMPORTATION_SAGE { get; set; }

    public DateTime? DATE_IMPORTATION_SAGE { get; set; }

    public bool? VALIDER_CORRECTION { get; set; }

    public decimal? HS { get; set; }

    public decimal? M_NUIT { get; set; }

    public decimal? M_DIMANCHE { get; set; }

    public decimal? M_FERIES { get; set; }

    public bool? VALIDER_HS { get; set; }

}
