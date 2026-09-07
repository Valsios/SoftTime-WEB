using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_HEUREDIMANCHE")]
public class T_HEUREDIMANCHE
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ID { get; set; }

    public string? BDD_SAGE { get; set; }

    public string? BDD_POINTEUSE { get; set; }

    public string? MATRICULE_SAGE { get; set; }

    public DateTime? Date_Dim { get; set; }

    public TimeSpan? HeureDebut { get; set; }

    public TimeSpan? HeureFin { get; set; }

    public decimal? Valeur { get; set; }

}
