using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_PLANNING_SAL")]
public class T_PLANNING_SAL
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ID { get; set; }

    public int IDSAL { get; set; }

    public int? NoSHIFT { get; set; }

    public DateTime? DateP { get; set; }

    public DateTime? HA { get; set; }

    public TimeSpan? Pause { get; set; }

    public DateTime? HD { get; set; }

    public bool? OffP { get; set; }

    public string? BDD_SAGE { get; set; }

    public string? BDD_POINTEUSE { get; set; }

}
