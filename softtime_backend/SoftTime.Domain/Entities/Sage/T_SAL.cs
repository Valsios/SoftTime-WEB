using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.Sage;

[Table("T_SAL")]
public class T_SAL
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int SA_CompteurNumero { get; set; }

    public string MatriculeSalarie { get; set; } = string.Empty;
    public string? Nom { get; set; }
    public string? Prenom { get; set; }
    public string? NumeroDeBadge { get; set; }
    public byte? SalarieDesactive { get; set; }
}
