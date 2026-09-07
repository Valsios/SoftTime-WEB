using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_RESPONSABLE")]
public class T_RESPONSABLE
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public decimal IDRESPONSABLE { get; set; }

    public decimal IDROLE { get; set; }

    public string? NOMRESPONSABLE { get; set; }

    public string? LOGIN { get; set; }

    public string? PASSWORD { get; set; }

    public string? MATRICULE { get; set; }

}
