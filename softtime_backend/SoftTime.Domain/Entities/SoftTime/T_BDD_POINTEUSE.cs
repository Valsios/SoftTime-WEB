using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_BDD_POINTEUSE")]
public class T_BDD_POINTEUSE
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ID { get; set; }

    public string? SERVEUR { get; set; }

    public string? TLOGIN { get; set; }

    public string? TMDP { get; set; }

    public bool? TYPE_AUTH { get; set; }

    public string? NOM_BD { get; set; }

    public string? TYPE_POINTAGE { get; set; }

    public bool? ACTIVE { get; set; }

}
