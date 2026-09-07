using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_CODE_CONSTANTE")]
public class T_CODE_CONSTANTE
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ID { get; set; }

    public string CATEGORIE { get; set; } = string.Empty;

    public string? INTITULE { get; set; }

    public string CODE_CONSTANTE { get; set; } = string.Empty;
}
