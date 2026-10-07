using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_PAIE_ECRITURE")]
public class T_PAIE_ECRITURE
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ID { get; set; }

    [Required]
    [MaxLength(128)]
    public string BDD_SAGE { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string TABLE_CIBLE { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string COL_MATRICULE { get; set; } = string.Empty;

    [Required]
    [MaxLength(32)]
    public string CATEGORIE { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string COL_VALEUR { get; set; } = string.Empty;
}
