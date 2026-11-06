namespace SoftTime.Application.DTOs;

public record AbsenceCodeDto(int Id, string? Intitule, bool? NotPay)
{
    public AbsenceCodeDto() : this(default, default, default) { }
}
