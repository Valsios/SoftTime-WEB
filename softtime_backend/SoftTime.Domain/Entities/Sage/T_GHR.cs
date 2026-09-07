using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.Sage;

[Table("T_GHR")]
public class T_GHR
{
    [Key]
    public string CodeNE { get; set; } = string.Empty;

    public string? Intitule { get; set; }
}
