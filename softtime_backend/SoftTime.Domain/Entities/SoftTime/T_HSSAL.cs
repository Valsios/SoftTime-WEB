using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_HSSAL")]
public class T_HSSAL
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ID { get; set; }

    public int? IDSAL { get; set; }

    public DateTime? Date { get; set; }

    public decimal? HT { get; set; }

}
