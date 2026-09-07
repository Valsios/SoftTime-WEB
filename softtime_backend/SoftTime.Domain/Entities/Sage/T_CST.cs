using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.Sage;

[Table("T_CST")]
public class T_CST
{
    public string CodeConstante { get; set; } = string.Empty;
    public short NoOrdre { get; set; }
    public short? CodeOperande1 { get; set; }
    public string? Intitule { get; set; }
}
