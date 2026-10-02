using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SoftTime.Domain.Entities.SoftTime;
[Table("T_BDD_SAGE")]
public class T_BDD_SAGE
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ID { get; set; }
    public string? SERVEUR { get; set; }
    public string? TLOGIN { get; set; }
    public string? TMDP { get; set; }
    public bool? TYPE_AUTH { get; set; }
    public string? NOM_BD { get; set; }
    public string TYPE_BASE { get; set; } = "STANDARD";
    public string? MAP_TABLE { get; set; }
    public string? MAP_COL_MATRICULE { get; set; }
    public string? MAP_COL_NOM { get; set; }
    public string? MAP_COL_PRENOM { get; set; }
    public string? MAP_COL_BADGE { get; set; }
    public string? MAP_COL_DEPARTEMENT { get; set; }
    public string? MAP_COL_SERVICE { get; set; }
    public string? MAP_COL_CODE_DEPARTEMENT { get; set; }
}