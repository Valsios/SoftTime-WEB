using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_CARDPAIE")]
public class T_CARDPAIE
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ID { get; set; }

    public string? SAGE_MATRICULE { get; set; }

    public string? BRANCHE { get; set; }

    public string? SAGE_NOM { get; set; }

    public string? SAGE_PRENOM { get; set; }

    public string? POINTEUSE_NUMERO { get; set; }

    public string? POINTEUSE_NOM { get; set; }

    public string? SAGE_SERVEUR { get; set; }

    public string? POINTEUSE_SERVEUR { get; set; }

    public string? SAGE_BDD { get; set; }

    public string? POINTEUSE_BDD { get; set; }

    public DateTime? DATE { get; set; }

}
