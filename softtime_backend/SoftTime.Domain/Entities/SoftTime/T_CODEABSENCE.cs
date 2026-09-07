using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_CODEABSENCE")]
public class T_CODEABSENCE
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ID_ABSENCE { get; set; }

    public string? INTITULE_ABSENCE { get; set; }

    public bool? NOT_PAY { get; set; }

}
