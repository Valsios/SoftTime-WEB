using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.Sage;

[Table("T_DEPARTEMENT")]
public class T_DEPARTEMENT
{
    [Key]
    public string Code { get; set; } = string.Empty;

    public string? Intitule { get; set; }
}
