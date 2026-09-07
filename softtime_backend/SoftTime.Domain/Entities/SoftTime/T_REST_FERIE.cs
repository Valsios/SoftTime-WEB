using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_REST_FERIE")]
public class T_REST_FERIE
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ID { get; set; }

    public string? BDD_SAGE { get; set; }

    public string? BDD_POINTEUSE { get; set; }

    public string? MATRICULE_SAGE { get; set; }

    public DateTime? DATE_DEBUT_SEMAINE { get; set; }

    public decimal? FERIE_DEBUT_SEMAINE { get; set; }

    public bool? ETAT { get; set; }

}
