namespace SoftTime.Domain.Entities.Pointeuse;

public class CheckInOut
{
    public int UserId { get; set; }

    public DateTime CheckTime { get; set; }

    public string? CheckType { get; set; }

    public int? VerifyCode { get; set; }

    public string? SensorId { get; set; }

    public string? Memoinfo { get; set; }

    public string? WorkCode { get; set; }

    public string? Sn { get; set; }

    public short? UserExtFmt { get; set; }
}
