using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_PLANNING")]
public class T_PLANNING
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public decimal IDPLANNING { get; set; }

    public int? NoSHIFT { get; set; }

    public TimeSpan? HA { get; set; }

    public TimeSpan? PAUSE { get; set; }

    public TimeSpan? HD { get; set; }

    public string? Intitule { get; set; }

}
