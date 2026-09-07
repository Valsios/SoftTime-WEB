using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_HSExoImp")]
public class T_HSExoImp
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int OID { get; set; }

    public string? Matricule { get; set; }

    public int? NumSal { get; set; }

    public decimal? EXO130 { get; set; }

    public decimal? EXO150 { get; set; }

    public decimal? I130 { get; set; }

    public decimal? I150 { get; set; }

    public DateTime? PeriodeDebut { get; set; }

    public DateTime? PeriodeFin { get; set; }

    public decimal? FERIE { get; set; }

    public decimal? NUIT { get; set; }

    public decimal? DIM { get; set; }

    public decimal? RETARD { get; set; }

    public decimal? ABSENCE { get; set; }

    public string? BDD_SAGE { get; set; }

    public string? BDD_POINTEUSE { get; set; }

}
