using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_impr_HS")]
public class T_impr_HS
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ID { get; set; }

    public string? MATR { get; set; }

    public string? EXO130 { get; set; }

    public string? EXO150 { get; set; }

    public string? IMPO130 { get; set; }

    public string? IMPO150 { get; set; }

    public string? MNUITS { get; set; }

    public string? MDIMANCHES { get; set; }

    public string? MFERIES { get; set; }

    public string? TOTALH { get; set; }

}
