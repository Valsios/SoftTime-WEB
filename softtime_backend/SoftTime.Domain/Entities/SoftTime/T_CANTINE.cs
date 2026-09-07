using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_CANTINE")]
public class T_CANTINE
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int IDCANTINE { get; set; }

    public string? NOM_BDD_SAGE { get; set; }

    public string? NOM_BDD_POINTEUSE { get; set; }

    public string? MATRICULE_SAGE { get; set; }

    public DateTime? DATEDEB { get; set; }

    public DateTime? DATEFIN { get; set; }

    public int? TOTAL_CANTINE { get; set; }

}
