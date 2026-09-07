using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_impr_RET")]
public class T_impr_RET
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ID { get; set; }

    public string? BDD_SAGE { get; set; }

    public string? BDD_POINTEUSE { get; set; }

    public string? MATRICULES { get; set; }

    public string? DATE_POINTAGE { get; set; }

    public TimeSpan? HEURE_ENTREE { get; set; }

    public TimeSpan? RETARDS { get; set; }

}
