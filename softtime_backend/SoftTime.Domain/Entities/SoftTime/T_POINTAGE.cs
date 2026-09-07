using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_POINTAGE")]
public class T_POINTAGE
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public decimal IDPOINTAGE { get; set; }

    public string? NOM_BDD_SAGE { get; set; }

    public string? MATRICULE_SAGE { get; set; }

    public DateTime? DATE_POINTAGE { get; set; }

    public TimeSpan? HEURE_POINTAGE { get; set; }

    public DateTime? DATE_IMPORTATION { get; set; }

    public string? NOM_BDD_POINTEUSE { get; set; }

    public decimal? IDUNIQUE_POINTAGE { get; set; }

    public string? TYPE_POINTAGE { get; set; }

}
