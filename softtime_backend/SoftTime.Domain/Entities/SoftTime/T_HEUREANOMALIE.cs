using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_HEUREANOMALIE")]
public class T_HEUREANOMALIE
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public decimal ID { get; set; }

    public string? MATRICULE_SAGE { get; set; }

    public DateTime? DATE_POINTAGE_IN { get; set; }

    public DateTime? DATE_POINTAGE_OUT { get; set; }

    public TimeSpan? H1 { get; set; }

    public TimeSpan? H2 { get; set; }

    public string? BDD_SAGE { get; set; }

    public string? BDD_POINTEUSE { get; set; }

    public TimeSpan? HENTREE { get; set; }

    public TimeSpan? HSORTIE { get; set; }

    public bool? ABS_AM { get; set; }

    public TimeSpan? HPE { get; set; }

    public TimeSpan? HPS { get; set; }

    public bool? FERIES_AM { get; set; }

    public bool? ABS_PM { get; set; }

    public bool? FERIES_PM { get; set; }

    public bool? FERIES { get; set; }

    public string? INTITULE_ABSENCE { get; set; }

}
