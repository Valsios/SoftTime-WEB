using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_impr_ABS")]
public class T_impr_ABS
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ID { get; set; }

    public string? BDD_SAGE { get; set; }

    public string? BDD_POINTEUSE { get; set; }

    public string? MATRICULES { get; set; }

    public string? DATE_POINTAGE { get; set; }

    public bool? ABSENCE_AM { get; set; }

    public bool? ABSENCE_PM { get; set; }

    public string? INTITULE_ABS { get; set; }

}
