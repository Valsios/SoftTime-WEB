using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("suivi_TBLCLN")]
public class suivi_TBLCLN
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public string? TABLES { get; set; }

    public string? COLONNES { get; set; }

    public string? TYPES { get; set; }

    public bool? ETATS { get; set; }

    public string? BASES { get; set; }

}
