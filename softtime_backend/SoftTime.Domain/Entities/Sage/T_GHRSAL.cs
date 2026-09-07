using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.Sage;

[Table("T_GHRSAL")]
public class T_GHRSAL
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int IdGhrSal { get; set; }

    public int NumSalarie { get; set; }
    public string CodeNE { get; set; } = string.Empty;
    public DateTime? PeriodeDebut { get; set; }
    public DateTime? PeriodeFin { get; set; }
    public double? Valeur { get; set; }
    public byte? ApresMidi { get; set; }
    public byte? Matin { get; set; }
    public byte? NombreParJour { get; set; }
}
