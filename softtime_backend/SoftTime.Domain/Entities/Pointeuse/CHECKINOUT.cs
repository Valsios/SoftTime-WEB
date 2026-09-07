using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.Pointeuse;

[Table("CHECKINOUT")]
public class CHECKINOUT
{
    [Key]
    public int USERID { get; set; }

    [Key]
    public DateTime CHECKTIME { get; set; }

    public string? CHECKTYPE { get; set; }

    public int? VERIFYCODE { get; set; }

    public string? SENSORID { get; set; }

    public string? Memoinfo { get; set; }

    public string? WorkCode { get; set; }

    public string? sn { get; set; }

    public short? UserExtFmt { get; set; }

}
