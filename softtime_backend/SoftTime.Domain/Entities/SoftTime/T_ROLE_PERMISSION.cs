using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_ROLE_PERMISSION")]
public class T_ROLE_PERMISSION
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ID { get; set; }

    public decimal IDROLE { get; set; }

    [MaxLength(128)]
    public string PERMISSION_CODE { get; set; } = string.Empty;

    public bool IS_GRANTED { get; set; } = true;
}
