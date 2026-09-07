using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_DROIT_ROLE")]
public class T_DROIT_ROLE
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public decimal IDDROITROLE { get; set; }

    public decimal IDDROIT { get; set; }

    public decimal IDROLE { get; set; }

}
