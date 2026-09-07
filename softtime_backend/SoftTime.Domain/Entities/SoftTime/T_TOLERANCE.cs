using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_TOLERANCES")]
public class T_TOLERANCE
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ID { get; set; }

    public int IDCATEGORIE { get; set; }

    public int TOLERANCE { get; set; }

    public bool TYPES_TOL { get; set; }

    public int? SORTIE { get; set; }

}
