using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_BDD_AUTORISE")]
public class T_BDD_AUTORISE
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int IDBDDAUTORISE { get; set; }

    public decimal IDRESPONSABLE { get; set; }

    public int IDBDD { get; set; }

}
