using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_SemainePeriode")]
public class T_SemainePeriode
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int OID { get; set; }

    public int? NoSemaine { get; set; }

    public DateTime? DebutSemaine { get; set; }

    public DateTime? FinSemaine { get; set; }

}
