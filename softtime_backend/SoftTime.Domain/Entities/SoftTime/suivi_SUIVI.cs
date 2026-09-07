using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("suivi_SUIVI")]
public class suivi_SUIVI
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Column("_TABLES")]
    public string? _TABLES { get; set; }

    [Column("_COLONNES")]
    public string? _COLONNES { get; set; }

    [Column("_USERS")]
    public string? _USERS { get; set; }

    [Column("_ACTIONS")]
    public string? _ACTIONS { get; set; }

    [Column("_DATETIMES")]
    public DateTime? _DATETIMES { get; set; }

    [Column("_NOUVEAUX")]
    public string? _NOUVEAUX { get; set; }

    [Column("_ANCIENS")]
    public string? _ANCIENS { get; set; }

    [Column("_BASES")]
    public string? _BASES { get; set; }

}
