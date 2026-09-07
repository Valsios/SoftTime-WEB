using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_CATEGORIE")]
public class T_CATEGORIE
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int IDCATEGORIE { get; set; }

    public string? INTITULE { get; set; }

    public int ID_CARDPAIE { get; set; }

    public TimeSpan? PAUSE { get; set; }

    public decimal? HEURESEMAINE { get; set; }

    public string? BDD_SAGE { get; set; }

    public string? BDD_POINTEUSE { get; set; }

}
