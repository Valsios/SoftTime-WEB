namespace SoftTime.Domain.Entities.Pointeuse;

public class CHECKINOUT
{
    public int USERID { get; set; }

    public DateTime CHECKTIME { get; set; }

    public string? CHECKTYPE { get; set; }

    public int? VERIFYCODE { get; set; }

    public string? SENSORID { get; set; }

    public string? Memoinfo { get; set; }

    public string? WorkCode { get; set; }

    public string? sn { get; set; }

    public short? UserExtFmt { get; set; }
}
