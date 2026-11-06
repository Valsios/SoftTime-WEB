namespace SoftTime.Domain.Entities.Pointeuse;

public class UserInfo
{
    public int UserId { get; set; }

    public string BadgeNumber { get; set; } = string.Empty;

    public string? Ssn { get; set; }

    public string? Name { get; set; }

    public string? Gender { get; set; }

    public string? Title { get; set; }

    public string? Pager { get; set; }

    public DateTime? Birthday { get; set; }

    public DateTime? HiredDay { get; set; }

    public string? Street { get; set; }

    public string? City { get; set; }

    public string? State { get; set; }

    public string? Zip { get; set; }

    public string? OPhone { get; set; }

    public string? FPhone { get; set; }

    public short? VerificationMethod { get; set; }

    public short? DefaultDeptId { get; set; }

    public short? SecurityFlags { get; set; }

    public short Att { get; set; }

    public short InLate { get; set; }

    public short OutEarly { get; set; }

    public short Overtime { get; set; }

    public short Sep { get; set; }

    public short Holiday { get; set; }

    public string? Minzu { get; set; }

    public string? Password { get; set; }

    public short LunchDuration { get; set; }

    public string? MVerifyPass { get; set; }

    public int? Privilege { get; set; }

    public short? InheritDeptSch { get; set; }

    public short? InheritDeptSchClass { get; set; }

    public short? AutoSchPlan { get; set; }

    public int? MinAutoSchInterval { get; set; }

    public short? RegisterOT { get; set; }

    public short? InheritDeptRule { get; set; }

    public short? EmPrivilege { get; set; }

    public string? CardNo { get; set; }

    public int? FaceGroup { get; set; }

    public int? AccGroup { get; set; }

    public int? UseAccGroupTZ { get; set; }

    public int? VerifyCode { get; set; }

    public int? Expires { get; set; }

    public int? ValidCount { get; set; }

    public DateTime? ValidTimeBegin { get; set; }

    public DateTime? ValidTimeEnd { get; set; }

    public int? TimeZone1 { get; set; }

    public int? TimeZone2 { get; set; }

    public int? TimeZone3 { get; set; }
}
