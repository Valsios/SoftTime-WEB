using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.Sage;

[Table("T_GHRCAL_SOCIETE")]
public class T_GHRCAL_SOCIETE
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int IdGHRCal { get; set; }

    public DateTime? PeriodeDebut { get; set; }
    public DateTime? PeriodeFin { get; set; }
    public byte? EtatJour { get; set; }
    public byte? Matin { get; set; }
    public byte? ApresMidi { get; set; }
}
