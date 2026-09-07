using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("suivi_T")]
public class suivi_T
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public string? S_TABLES { get; set; }

    public string? S_COLONNES { get; set; }

    public string? S_TYPES { get; set; }

    public string? BASES { get; set; }

}
