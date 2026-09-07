using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_Semaine")]
public class T_Semaine
{
    public string? CodeSemain { get; set; }

    public DateTime? DateDeb { get; set; }

    public DateTime? DateFin { get; set; }

}
