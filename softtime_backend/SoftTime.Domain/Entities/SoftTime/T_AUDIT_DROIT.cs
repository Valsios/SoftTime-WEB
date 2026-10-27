using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_AUDIT_DROIT")]
public class T_AUDIT_DROIT
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ID { get; set; }

    public DateTime DATE_ACTION { get; set; } = DateTime.UtcNow;

    public decimal ID_OPERATEUR { get; set; }

    [MaxLength(100)]
    public string LOGIN_OPERATEUR { get; set; } = string.Empty;

    [MaxLength(20)]
    public string TYPE_CIBLE { get; set; } = string.Empty;

    public decimal ID_CIBLE { get; set; }

    [MaxLength(100)]
    public string? NOM_CIBLE { get; set; }

    [MaxLength(50)]
    public string ACTION { get; set; } = string.Empty;

    public string? PERMS_AVANT { get; set; }

    public string? PERMS_APRES { get; set; }
}
