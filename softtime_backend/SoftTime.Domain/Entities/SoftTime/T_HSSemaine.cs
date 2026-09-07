using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_HSSemaine")]
public class T_HSSemaine
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int OID { get; set; }

    public string? Matricule { get; set; }

    public int? SalNum { get; set; }

    public decimal? TotalHeure { get; set; }

    public decimal? HS { get; set; }

    public decimal? HS1 { get; set; }

    public decimal? HS2 { get; set; }

    public DateTime? PeriodeDebut { get; set; }

    public DateTime? PeriodeFin { get; set; }

    public int? Semaine { get; set; }

    public string? BDD_SAGE { get; set; }

    public string? BDD_POINTEUSE { get; set; }

}
