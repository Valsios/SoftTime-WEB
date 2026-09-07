using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_FERIE")]
public class T_FERIE
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ID { get; set; }

    public string? INTITULE { get; set; }

    public DateTime? DATE { get; set; }

    public string? BDD_SAGE { get; set; }

}
